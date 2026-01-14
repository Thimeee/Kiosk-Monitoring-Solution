using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MQTTnet.Protocol;
using Renci.SshNet;
using Renci.SshNet.Async;

namespace SFTPService.Helper
{
    public class SftpFileService : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _user;
        private readonly string _pass;
        private readonly LoggerService _log;
        private readonly MQTTHelper _mqtt;

        private const int LargeBufferSize = 1024 * 1024 * 4; // 4MB for large files
        private const int SmallBufferSize = 1024 * 64; // 64KB for small files
        private const long SmallFileThreshold = 1024 * 1024 * 10; // 10MB threshold
        private const int MaxRetries = 5;
        private const int RetryDelayMs = 500;
        private const int ProgressReportIntervalMs = 1000;
        private const double ProgressReportThresholdPercent = 5.0;

        // Connection pool for reusing SFTP connections
        private readonly ConcurrentBag<SftpClient> _connectionPool = new();
        private readonly SemaphoreSlim _poolLock = new SemaphoreSlim(10, 10); // Max 10 concurrent SFTP operations
        private readonly Timer _cleanupTimer;

        public SftpFileService(IConfiguration config, LoggerService log, MQTTHelper mqtt)
        {
            _host = config["Sftp:Host"] ?? "MAIN_SERVER_IP";
            _user = config["Sftp:Username"] ?? "sftp_user";
            _pass = config["Sftp:Password"] ?? "password";
            _port = int.TryParse(config["Sftp:Port"], out var p) ? p : 22;
            _log = log;
            _mqtt = mqtt;

            // Cleanup idle connections every 5 minutes
            _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private void CleanupIdleConnections(object state)
        {
            var removedCount = 0;
            while (_connectionPool.TryTake(out var client))
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                    client.Dispose();
                    removedCount++;
                }
                catch { }
            }

            if (removedCount > 0)
            {
                _log.WriteLog("SFTP Cleanup", $"Cleaned up {removedCount} idle connections").Wait();
            }
        }

        private async Task<SftpClient> GetConnectionAsync()
        {
            if (_connectionPool.TryTake(out var client))
            {
                if (client.IsConnected)
                {
                    return client;
                }
                else
                {
                    try
                    {
                        client.Connect();
                        return client;
                    }
                    catch
                    {
                        client.Dispose();
                    }
                }
            }

            // Create new connection
            var newClient = new SftpClient(_host, _port, _user, _pass);
            await Task.Run(() => newClient.Connect());
            return newClient;
        }

        private void ReturnConnection(SftpClient client)
        {
            if (client != null && client.IsConnected)
            {
                _connectionPool.Add(client);
            }
            else
            {
                client?.Dispose();
            }
        }

        public string ConvertRealPath(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                throw new ArgumentException("Remote path is empty");

            // Convert Windows slashes to Linux
            remotePath = remotePath.Replace("\\", "/");

            // Remove drive letter (C:, D:, etc)
            if (remotePath.Length > 2 && remotePath[1] == ':')
                remotePath = remotePath.Substring(2);

            // Ensure starting slash
            if (!remotePath.StartsWith("/"))
                remotePath = "/" + remotePath;

            // Remove duplicate slashes
            while (remotePath.Contains("//"))
                remotePath = remotePath.Replace("//", "/");

            return remotePath;
        }

        public async Task DownloadFileAsync(
            string remotePathBefore,
            string localPathBefore,
            string userId,
            string branchID,
            string jobId,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var remotePath = ConvertRealPath(remotePathBefore);
            var localPath = ConvertRealPath(localPathBefore);

            await _poolLock.WaitAsync(cancellationToken);
            SftpClient sftp = null;

            try
            {
                sftp = await GetConnectionAsync();

                // Check if file exists
                if (!sftp.Exists(remotePath))
                {
                    await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 0, "FileNotFound");
                    return;
                }

                var remoteFileSize = sftp.GetAttributes(remotePath).Size;
                long offset = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;

                // Validate offset
                if (offset > remoteFileSize) offset = 0;

                // Already downloaded
                if (offset == remoteFileSize && remoteFileSize > 0)
                {
                    await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 2, "AlreadyDownloaded");
                    return;
                }

                // Determine buffer size based on file size
                int bufferSize = remoteFileSize > SmallFileThreshold ? LargeBufferSize : SmallBufferSize;

                await DownloadFileWithResumeAsync(
                    sftp,
                    remotePath,
                    localPath,
                    remoteFileSize,
                    offset,
                    bufferSize,
                    userId,
                    jobId,
                    branchID,
                    progress,
                    cancellationToken);

                await _log.WriteLog("SFTP Success", $"Downloaded '{remotePath}' to '{localPath}'");
            }
            catch (OperationCanceledException)
            {
                await _log.WriteLog("SFTP Download", "Download cancelled by user");
                await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 0, "Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SFTP Error", $"Download failed: {remotePathBefore}", 3);
                await _log.WriteLog("SFTP Exception", $"{ex}", 3);
                await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 0, "Failed");
                throw;
            }
            finally
            {
                ReturnConnection(sftp);
                _poolLock.Release();
            }
        }

        private async Task DownloadFileWithResumeAsync(
            SftpClient sftp,
            string remotePath,
            string localPath,
            long remoteFileSize,
            long initialOffset,
            int bufferSize,
            string userId,
            string jobId,
            string branchID,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            long offset = initialOffset;
            bool isFirstChunk = (offset == 0);

            while (attempt < MaxRetries)
            {
                attempt++;

                try
                {
                    using var fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    using var remoteStream = sftp.OpenRead(remotePath);
                    remoteStream.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;

                    var lastPublish = DateTime.MinValue;
                    double lastReportedPercent = 0;

                    while (offset < remoteFileSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (bytesRead <= 0) break;

                        await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        offset += bytesRead;

                        // Report first chunk only once
                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 1, "Started");
                        }

                        // Report progress
                        double percent = Math.Round((double)offset / remoteFileSize * 100, 2);
                        if ((DateTime.Now - lastPublish).TotalMilliseconds >= ProgressReportIntervalMs &&
                            percent - lastReportedPercent >= ProgressReportThresholdPercent)
                        {
                            lastReportedPercent = percent;
                            lastPublish = DateTime.Now;

                            await PublishProgress(userId, jobId, branchID, "DownloadProgress", 1, percent, remoteFileSize, offset);
                        }
                    }

                    // Success
                    await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 2, "Completed");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Download Attempt", $"Attempt {attempt}/{MaxRetries} failed: {ex.Message}", 2);

                    if (attempt >= MaxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(RetryDelayMs * attempt, cancellationToken);

                    // Reconnect if needed
                    if (!sftp.IsConnected)
                    {
                        try
                        {
                            sftp.Connect();
                        }
                        catch
                        {
                            // Connection failed, will retry
                        }
                    }
                }
            }
        }

        public async Task UploadFileAsync(
            string localPathBefore,
            string remotePathBefore,
            string userId,
            string branchID,
            string jobId,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var localPath = ConvertRealPath(localPathBefore);
            var remotePath = ConvertRealPath(remotePathBefore);

            await _poolLock.WaitAsync(cancellationToken);
            SftpClient sftp = null;

            try
            {
                sftp = await GetConnectionAsync();

                // Check if local file exists
                if (!File.Exists(localPath))
                {
                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 0, "FileNotFound");
                    return;
                }

                long localFileSize = new FileInfo(localPath).Length;
                long offset = sftp.Exists(remotePath) ? sftp.GetAttributes(remotePath).Size : 0;

                // Validate offset
                if (offset > localFileSize) offset = 0;

                // Already uploaded
                if (offset == localFileSize && localFileSize > 0)
                {
                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 2, "AlreadyUploaded");
                    return;
                }

                // Determine buffer size based on file size
                int bufferSize = localFileSize > SmallFileThreshold ? LargeBufferSize : SmallBufferSize;

                await UploadFileWithResumeAsync(
                    sftp,
                    localPath,
                    remotePath,
                    localFileSize,
                    offset,
                    bufferSize,
                    userId,
                    jobId,
                    branchID,
                    progress,
                    cancellationToken);

                await _log.WriteLog("SFTP Success", $"Uploaded '{localPath}' to '{remotePath}'");
            }
            catch (OperationCanceledException)
            {
                await _log.WriteLog("SFTP Upload", "Upload cancelled by user");
                await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 0, "Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SFTP Error", $"Upload failed: {localPathBefore}", 3);
                await _log.WriteLog("SFTP Exception", $"{ex}", 3);
                await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 0, "Failed");
                throw;
            }
            finally
            {
                ReturnConnection(sftp);
                _poolLock.Release();
            }
        }

        private async Task UploadFileWithResumeAsync(
            SftpClient sftp,
            string localPath,
            string remotePath,
            long localFileSize,
            long initialOffset,
            int bufferSize,
            string userId,
            string jobId,
            string branchID,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            long offset = initialOffset;
            bool isFirstChunk = (offset == 0);

            while (attempt < MaxRetries)
            {
                attempt++;

                try
                {
                    using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    // Ensure remote directory exists
                    var remoteDir = Path.GetDirectoryName(remotePath)?.Replace("\\", "/");
                    if (!string.IsNullOrEmpty(remoteDir) && !sftp.Exists(remoteDir))
                    {
                        sftp.CreateDirectory(remoteDir);
                    }

                    using var remoteStream = sftp.Open(remotePath, FileMode.OpenOrCreate, FileAccess.Write);
                    remoteStream.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;

                    var lastPublish = DateTime.MinValue;
                    double lastReportedPercent = 0;

                    while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await remoteStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        offset += bytesRead;

                        // Report first chunk only once
                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 1, "Started");
                        }

                        // Report progress
                        double percent = Math.Round((double)offset / localFileSize * 100, 2);
                        if ((DateTime.Now - lastPublish).TotalMilliseconds >= ProgressReportIntervalMs &&
                            percent - lastReportedPercent >= ProgressReportThresholdPercent)
                        {
                            lastReportedPercent = percent;
                            lastPublish = DateTime.Now;

                            await PublishProgress(userId, jobId, branchID, "UploadProgress", 1, percent, localFileSize, offset);
                        }
                    }

                    // Success
                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 2, "Completed");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Upload Attempt", $"Attempt {attempt}/{MaxRetries} failed: {ex.Message}", 2);

                    if (attempt >= MaxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(RetryDelayMs * attempt, cancellationToken);

                    // Reconnect if needed
                    if (!sftp.IsConnected)
                    {
                        try
                        {
                            sftp.Connect();
                        }
                        catch
                        {
                            // Connection failed, will retry
                        }
                    }
                }
            }
        }

        // Helper method to publish job response
        private async Task PublishJobResponse(
            string userId,
            string jobId,
            string branchID,
            string responseType,
            int status,
            string message)
        {
            try
            {
                var response = new BranchJobResponse<JobDownloadResponse>
                {
                    jobUser = userId,
                    jobEndTime = DateTime.Now,
                    jobId = jobId,
                    jobRsValue = new JobDownloadResponse
                    {
                        jobStatus = status,
                        jobMsg = message
                    }
                };

                var qos = status == 2 || status == 0
                    ? MqttQualityOfServiceLevel.ExactlyOnce
                    : MqttQualityOfServiceLevel.AtMostOnce;

                await _mqtt.PublishToServer(response, $"server/{branchID}/SFTP/{responseType}", qos, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Publish Error", $"Failed to publish {responseType}: {ex}", 3);
            }
        }

        // Helper method to publish progress
        private async Task PublishProgress(
            string userId,
            string jobId,
            string branchID,
            string progressType,
            int status,
            double percent,
            long totalBytes,
            long completedBytes)
        {
            try
            {
                var progressObj = new BranchJobResponse<JobDownloadResponse>
                {
                    jobUser = userId,
                    jobEndTime = DateTime.Now,
                    jobId = jobId,
                    jobRsValue = new JobDownloadResponse
                    {
                        jobStatus = status,
                        jobProgress = percent,
                        jobTotalBytes = totalBytes,
                        jobDownloadedBytes = completedBytes
                    }
                };

                await _mqtt.PublishToServer(progressObj, $"server/{branchID}/SFTP/{progressType}", MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Progress Error", $"Failed to publish progress: {ex}", 3);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _poolLock?.Dispose();

            while (_connectionPool.TryTake(out var client))
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                    client.Dispose();
                }
                catch { }
            }
        }
    }
}