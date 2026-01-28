using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MQTTnet.Protocol;
using Renci.SshNet;

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

        // Buffer sizes optimized for all file sizes
        private const int SmallBufferSize = 1024 * 64;          // 64KB for small files
        private const int MediumBufferSize = 1024 * 1024 * 4;   // 4MB for medium files
        private const int LargeBufferSize = 1024 * 1024 * 8;    // 8MB for large files
        private const int MassiveBufferSize = 1024 * 1024 * 16; // 16MB for massive files

        // Timeout tiers
        private const int ConnectionTimeoutSeconds = 30;
        private const int SmallFileTimeoutSeconds = 180;      // 3 min (< 100MB)
        private const int MediumFileTimeoutSeconds = 900;     // 15 min (100MB-500MB)
        private const int LargeFileTimeoutSeconds = 2400;     // 40 min (500MB-2GB)
        private const int ChunkTimeout = 600;                 // 10 min per chunk

        //File size thresholds
        private const long SmallFileThreshold = 1024L * 1024 * 100;           // 100MB
        private const long MediumFileThreshold = 1024L * 1024 * 500;          // 500MB
        private const long LargeFileThreshold = 1024L * 1024L * 1024 * 2;     // 2GB
        private const long MassiveFileThreshold = 1024L * 1024L * 1024 * 2;   // 2GB (use chunking)

        // Chunked download settings
        private const long ChunkSize = 1024L * 1024 * 100;    // 100MB chunks
        private const int MaxChunkRetries = 5;

        // Retry settings
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 3000;
        private const int ProgressReportIntervalMs = 1000;
        private const double ProgressReportThresholdPercent = 2.0;

        // Connection pool settings
        private const int MaxConnectionAge = 15; // Minutes
        private const int MaxPoolSize = 5;

        // Feature flags
        private const bool EnableIntegrityCheck = true;
        private const bool EnableAdaptiveTimeout = false;

        // Connection pool with metadata
        private readonly ConcurrentBag<ConnectionInfo> _connectionPool = new();
        private readonly SemaphoreSlim _poolLock = new SemaphoreSlim(5, 5);
        private readonly Timer _cleanupTimer;

        private class ConnectionInfo
        {
            public SftpClient Client { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastUsedAt { get; set; }
        }

        public SftpFileService(IConfiguration config, LoggerService log, MQTTHelper mqtt)
        {
            _host = config["Sftp:Host"] ?? "MAIN_SERVER_IP";
            _user = config["Sftp:Username"] ?? "sftp_user";
            _pass = config["Sftp:Password"] ?? "password";
            _port = int.TryParse(config["Sftp:Port"], out var p) ? p : 22;
            _log = log;
            _mqtt = mqtt;

            _cleanupTimer = new Timer(CleanupIdleConnections, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private void CleanupIdleConnections(object state)
        {
            var removedCount = 0;
            var cutoffTime = DateTime.Now.AddMinutes(-MaxConnectionAge);

            var connectionsToKeep = new List<ConnectionInfo>();

            while (_connectionPool.TryTake(out var connInfo))
            {
                if (connInfo.LastUsedAt < cutoffTime || !connInfo.Client.IsConnected)
                {
                    try
                    {
                        if (connInfo.Client.IsConnected)
                            connInfo.Client.Disconnect();
                        connInfo.Client.Dispose();
                        removedCount++;
                    }
                    catch { }
                }
                else
                {
                    connectionsToKeep.Add(connInfo);
                }
            }

            foreach (var conn in connectionsToKeep)
            {
                _connectionPool.Add(conn);
            }

            if (removedCount > 0)
            {
                _log.WriteLog("SFTP Cleanup",
                    $"Cleaned up {removedCount} stale connections (Pool: {_connectionPool.Count})").Wait();
            }
        }

        private int GetOperationTimeout(long fileSize)
        {
            if (fileSize <= SmallFileThreshold)
                return SmallFileTimeoutSeconds;
            else if (fileSize <= MediumFileThreshold)
                return MediumFileTimeoutSeconds;
            else if (fileSize <= LargeFileThreshold)
                return LargeFileTimeoutSeconds;
            else
                return ChunkTimeout;
        }

        private int GetBufferSize(long fileSize)
        {
            if (fileSize <= SmallFileThreshold)
                return SmallBufferSize;
            else if (fileSize <= MediumFileThreshold)
                return MediumBufferSize;
            else if (fileSize <= LargeFileThreshold)
                return LargeBufferSize;
            else
                return MassiveBufferSize;
        }

        private async Task<SftpClient> GetConnectionAsync(long fileSize = 0)
        {
            var now = DateTime.Now;
            var cutoffTime = now.AddMinutes(-MaxConnectionAge);

            // Try to get existing connection
            while (_connectionPool.TryTake(out var connInfo))
            {
                if (connInfo.LastUsedAt >= cutoffTime && connInfo.Client.IsConnected)
                {
                    connInfo.LastUsedAt = now;

                    int operationTimeout = GetOperationTimeout(fileSize);
                    connInfo.Client.OperationTimeout = TimeSpan.FromSeconds(operationTimeout);

                    return connInfo.Client;
                }
                else
                {
                    try
                    {
                        if (connInfo.Client.IsConnected)
                            connInfo.Client.Disconnect();
                        connInfo.Client.Dispose();
                    }
                    catch { }
                }
            }

            // Create new connection
            string fileSizeStr = fileSize > 0
                ? $" for {fileSize / 1024.0 / 1024.0 / 1024.0:F2}GB file"
                : "";
            await _log.WriteLog("SFTP", $"Creating new connection to {_host}:{_port}{fileSizeStr}...");

            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                _host,
                _port,
                _user,
                new PasswordAuthenticationMethod(_user, _pass))
            {
                Timeout = TimeSpan.FromSeconds(ConnectionTimeoutSeconds),
                RetryAttempts = 2
            };

            int opTimeout = GetOperationTimeout(fileSize);

            var client = new SftpClient(connectionInfo)
            {
                OperationTimeout = TimeSpan.FromSeconds(opTimeout),
                BufferSize = (uint)GetBufferSize(fileSize),
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            await Task.Run(() => client.Connect());

            await _log.WriteLog("SFTP",
                $"✓ Connected (Timeout: {opTimeout}s, Buffer: {GetBufferSize(fileSize) / 1024 / 1024}MB)");

            return client;
        }

        private void ReturnConnection(SftpClient client)
        {
            if (client == null) return;

            if (client.IsConnected && _connectionPool.Count < MaxPoolSize)
            {
                _connectionPool.Add(new ConnectionInfo
                {
                    Client = client,
                    CreatedAt = DateTime.Now,
                    LastUsedAt = DateTime.Now
                });
            }
            else
            {
                try
                {
                    if (client.IsConnected)
                        client.Disconnect();
                    client.Dispose();
                }
                catch { }
            }
        }

        public string ConvertRealPath(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                throw new ArgumentException("Remote path is empty");

            remotePath = remotePath.Replace("\\", "/");
            if (remotePath.Length > 2 && remotePath[1] == ':')
                remotePath = remotePath.Substring(2);
            if (!remotePath.StartsWith("/"))
                remotePath = "/" + remotePath;
            while (remotePath.Contains("//"))
                remotePath = remotePath.Replace("//", "/");

            return remotePath;
        }

        /// <summary>
        /// Verify file integrity using SHA256 checksum
        /// </summary>
        private async Task<bool> VerifyFileIntegrity(string filePath, string expectedHash)
        {
            if (!EnableIntegrityCheck) return true;
            if (string.IsNullOrEmpty(expectedHash)) return true;

            try
            {
                await _log.WriteLog("SFTP Verify", "Computing file checksum...");

                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await sha256.ComputeHashAsync(stream);
                var actualHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                bool isValid = actualHash == expectedHash.ToLowerInvariant();

                if (isValid)
                {
                    await _log.WriteLog("SFTP Verify", "✓ File integrity verified");
                }
                else
                {
                    await _log.WriteLog("SFTP Verify Error",
                        $"Checksum mismatch! Expected: {expectedHash}, Got: {actualHash}", 3);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SFTP Verify Error", $"Verification failed: {ex.Message}", 3);
                return false;
            }
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

                if (!sftp.Exists(remotePath))
                {
                    await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 0, "FileNotFound");
                    return;
                }

                var remoteFileSize = sftp.GetAttributes(remotePath).Size;

                // Determine download strategy based on file size
                if (remoteFileSize > MassiveFileThreshold)
                {
                    await _log.WriteLog("SFTP Download",
                        $"MASSIVE file: {remoteFileSize / 1024.0 / 1024.0 / 1024.0:F2}GB - Using CHUNKED download");

                    await DownloadFileInChunksAsync(
                        sftp, remotePath, localPath, remoteFileSize,
                        userId, jobId, branchID, progress, cancellationToken);
                }
                else
                {
                    string sizeCategory = remoteFileSize <= SmallFileThreshold ? "Small" :
                                         remoteFileSize <= MediumFileThreshold ? "Medium" : "Large";

                    int operationTimeout = GetOperationTimeout(remoteFileSize);
                    int bufferSize = GetBufferSize(remoteFileSize);

                    sftp.OperationTimeout = TimeSpan.FromSeconds(operationTimeout);
                    sftp.BufferSize = (uint)bufferSize;

                    await _log.WriteLog("SFTP Download",
                        $"{sizeCategory}: {remoteFileSize / 1024.0 / 1024.0:F2}MB | " +
                        $"Timeout: {operationTimeout}s | Buffer: {bufferSize / 1024 / 1024}MB");

                    long offset = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
                    if (offset > remoteFileSize) offset = 0;

                    if (offset == remoteFileSize && remoteFileSize > 0)
                    {
                        await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 2, "AlreadyDownloaded");
                        return;
                    }

                    await DownloadFileWithResumeAsync(
                        sftp, remotePath, localPath, remoteFileSize, offset, bufferSize,
                        userId, jobId, branchID, progress, cancellationToken);
                }

                await _log.WriteLog("SFTP Success",
                    $"Downloaded {remoteFileSize / 1024.0 / 1024.0 / 1024.0:F2}GB: '{remotePath}'");
            }
            catch (OperationCanceledException)
            {
                await _log.WriteLog("SFTP Download", "Download cancelled");
                await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 0, "Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SFTP Error", $"Download failed: {remotePathBefore}", 3);
                await _log.WriteLog("SFTP Exception", $"{ex.Message}", 3);
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

            DateTime downloadStartTime = DateTime.Now;
            long bytesAtLastCheck = offset;
            DateTime lastSpeedCheck = downloadStartTime;

            while (attempt < MaxRetries)
            {
                attempt++;

                try
                {
                    using var fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write,
                        FileShare.None, bufferSize, true);
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

                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 1, "Started");
                        }

                        var now = DateTime.Now;
                        if ((now - lastSpeedCheck).TotalSeconds >= 10)
                        {
                            long bytesSinceLastCheck = offset - bytesAtLastCheck;
                            double secondsElapsed = (now - lastSpeedCheck).TotalSeconds;
                            double speedMbps = (bytesSinceLastCheck * 8 / 1024.0 / 1024.0) / secondsElapsed;

                            double remainingMB = (remoteFileSize - offset) / 1024.0 / 1024.0;
                            double etaSeconds = remainingMB * 8 / speedMbps;

                            await _log.WriteLog("SFTP Speed",
                                $"Speed: {speedMbps:F2} Mbps | Remaining: {remainingMB:F2}MB | ETA: {etaSeconds:F0}s");

                            bytesAtLastCheck = offset;
                            lastSpeedCheck = now;
                        }

                        double percent = Math.Round((double)offset / remoteFileSize * 100, 2);
                        if ((now - lastPublish).TotalMilliseconds >= ProgressReportIntervalMs &&
                            percent - lastReportedPercent >= ProgressReportThresholdPercent)
                        {
                            lastReportedPercent = percent;
                            lastPublish = now;

                            await PublishProgress(userId, jobId, branchID, "DownloadProgress",
                                1, percent, remoteFileSize, offset);
                        }
                    }

                    var totalTime = (DateTime.Now - downloadStartTime).TotalSeconds;
                    double avgSpeedMbps = (remoteFileSize * 8 / 1024.0 / 1024.0) / totalTime;

                    await _log.WriteLog("SFTP Success",
                        $"Download completed in {totalTime:F0}s | Avg speed: {avgSpeedMbps:F2} Mbps");

                    await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 2, "Completed");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Download Attempt",
                        $"Attempt {attempt}/{MaxRetries} failed at {offset / 1024.0 / 1024.0:F2}MB: {ex.Message}", 2);

                    if (attempt >= MaxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(RetryDelayMs * attempt, cancellationToken);

                    try
                    {
                        if (sftp.IsConnected)
                            sftp.Disconnect();
                        sftp.Dispose();
                    }
                    catch { }

                    sftp = await GetConnectionAsync(remoteFileSize);
                }
            }
        }

        private async Task DownloadFileInChunksAsync(
            SftpClient sftp,
            string remotePath,
            string localPath,
            long totalFileSize,
            string userId,
            string jobId,
            string branchID,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            string progressFile = localPath + ".progress";
            long startOffset = 0;

            if (File.Exists(progressFile))
            {
                try
                {
                    string offsetStr = await File.ReadAllTextAsync(progressFile);
                    if (long.TryParse(offsetStr, out long savedOffset))
                    {
                        startOffset = savedOffset;
                        await _log.WriteLog("SFTP Resume",
                            $"Resuming from {startOffset / 1024.0 / 1024.0 / 1024.0:F2}GB " +
                            $"({startOffset * 100.0 / totalFileSize:F2}%)");
                    }
                }
                catch { }
            }

            using var fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write,
                FileShare.None, MassiveBufferSize, true);
            fs.Seek(startOffset, SeekOrigin.Begin);

            long currentOffset = startOffset;
            int chunkNumber = (int)(startOffset / ChunkSize);
            int totalChunks = (int)((totalFileSize + ChunkSize - 1) / ChunkSize);

            await _log.WriteLog("SFTP Chunked",
                $"Starting chunked download: {totalChunks} chunks of {ChunkSize / 1024 / 1024}MB each");

            await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 1,
                $"Downloading in {totalChunks} chunks");

            DateTime downloadStartTime = DateTime.Now;
            long bytesAtLastCheck = currentOffset;
            DateTime lastSpeedCheck = downloadStartTime;

            while (currentOffset < totalFileSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                chunkNumber++;
                long chunkStart = currentOffset;
                long chunkEnd = Math.Min(chunkStart + ChunkSize, totalFileSize);
                long chunkLength = chunkEnd - chunkStart;

                await _log.WriteLog("SFTP Chunk",
                    $"Downloading chunk {chunkNumber}/{totalChunks}: " +
                    $"{chunkStart / 1024.0 / 1024.0:F2}MB - {chunkEnd / 1024.0 / 1024.0:F2}MB " +
                    $"({chunkLength / 1024.0 / 1024.0:F2}MB)");

                bool chunkSuccess = false;
                int chunkAttempt = 0;

                while (chunkAttempt < MaxChunkRetries && !chunkSuccess)
                {
                    chunkAttempt++;

                    try
                    {
                        if (!sftp.IsConnected)
                        {
                            await _log.WriteLog("SFTP Chunk", "Reconnecting...");
                            sftp.Connect();
                        }

                        sftp.OperationTimeout = TimeSpan.FromSeconds(ChunkTimeout);

                        using var remoteStream = sftp.OpenRead(remotePath);
                        remoteStream.Seek(chunkStart, SeekOrigin.Begin);

                        byte[] buffer = new byte[MassiveBufferSize];
                        long chunkBytesRead = 0;

                        var lastProgressReport = DateTime.MinValue;
                        double lastReportedPercent = 0;

                        while (chunkBytesRead < chunkLength)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int toRead = (int)Math.Min(buffer.Length, chunkLength - chunkBytesRead);
                            int bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);

                            if (bytesRead <= 0) break;

                            await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                            chunkBytesRead += bytesRead;
                            currentOffset += bytesRead;

                            var now = DateTime.Now;
                            double overallPercent = Math.Round((double)currentOffset / totalFileSize * 100, 2);

                            if ((now - lastProgressReport).TotalMilliseconds >= ProgressReportIntervalMs &&
                                overallPercent - lastReportedPercent >= 1.0)
                            {
                                lastReportedPercent = overallPercent;
                                lastProgressReport = now;

                                await PublishProgress(userId, jobId, branchID, "DownloadProgress",
                                    1, overallPercent, totalFileSize, currentOffset);
                            }

                            if ((now - lastSpeedCheck).TotalSeconds >= 10)
                            {
                                long bytesSinceLastCheck = currentOffset - bytesAtLastCheck;
                                double secondsElapsed = (now - lastSpeedCheck).TotalSeconds;
                                double speedMbps = (bytesSinceLastCheck * 8 / 1024.0 / 1024.0) / secondsElapsed;

                                double remainingGB = (totalFileSize - currentOffset) / 1024.0 / 1024.0 / 1024.0;
                                double etaMinutes = (remainingGB * 1024 * 8 / speedMbps) / 60;

                                await _log.WriteLog("SFTP Speed",
                                    $"Chunk {chunkNumber}/{totalChunks} | Speed: {speedMbps:F2} Mbps | " +
                                    $"Remaining: {remainingGB:F2}GB | ETA: {etaMinutes:F0}m");

                                bytesAtLastCheck = currentOffset;
                                lastSpeedCheck = now;
                            }
                        }

                        chunkSuccess = true;

                        await File.WriteAllTextAsync(progressFile, currentOffset.ToString());

                        await _log.WriteLog("SFTP Chunk",
                            $"✓ Chunk {chunkNumber}/{totalChunks} completed " +
                            $"({currentOffset * 100.0 / totalFileSize:F2}% total)");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLog("SFTP Chunk Error",
                            $"Chunk {chunkNumber} attempt {chunkAttempt}/{MaxChunkRetries} failed: {ex.Message}",
                            chunkAttempt >= MaxChunkRetries ? 3 : 2);

                        if (chunkAttempt >= MaxChunkRetries)
                        {
                            throw new Exception(
                                $"Chunk {chunkNumber}/{totalChunks} failed after {MaxChunkRetries} attempts", ex);
                        }

                        await Task.Delay(RetryDelayMs * chunkAttempt, cancellationToken);

                        try
                        {
                            if (sftp.IsConnected)
                                sftp.Disconnect();
                            sftp.Dispose();
                        }
                        catch { }

                        sftp = await GetConnectionAsync(totalFileSize);
                    }
                }

                if (!chunkSuccess)
                {
                    throw new Exception($"Failed to download chunk {chunkNumber}/{totalChunks}");
                }
            }

            fs.Close();

            var totalTime = (DateTime.Now - downloadStartTime).TotalSeconds;
            double avgSpeedGbps = (totalFileSize * 8 / 1024.0 / 1024.0 / 1024.0) / totalTime;

            await _log.WriteLog("SFTP Success",
                $"Chunked download completed: {totalFileSize / 1024.0 / 1024.0 / 1024.0:F2}GB in {totalTime / 60:F0}m " +
                $"| Avg speed: {avgSpeedGbps:F2} Gbps");

            try
            {
                File.Delete(progressFile);
            }
            catch { }

            await PublishJobResponse(userId, jobId, branchID, "DownloadResponse", 2, "Completed");
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

                if (!File.Exists(localPath))
                {
                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 0, "FileNotFound");
                    return;
                }

                long localFileSize = new FileInfo(localPath).Length;
                long offset = sftp.Exists(remotePath) ? sftp.GetAttributes(remotePath).Size : 0;

                if (offset > localFileSize) offset = 0;

                if (offset == localFileSize && localFileSize > 0)
                {
                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 2, "AlreadyUploaded");
                    return;
                }

                int bufferSize = GetBufferSize(localFileSize);
                int operationTimeout = GetOperationTimeout(localFileSize);

                sftp.OperationTimeout = TimeSpan.FromSeconds(operationTimeout);
                sftp.BufferSize = (uint)bufferSize;

                await UploadFileWithResumeAsync(
                    sftp, localPath, remotePath, localFileSize, offset, bufferSize,
                    userId, jobId, branchID, progress, cancellationToken);

                await _log.WriteLog("SFTP Success", $"Uploaded '{localPath}' to '{remotePath}'");
            }
            catch (OperationCanceledException)
            {
                await _log.WriteLog("SFTP Upload", "Upload cancelled");
                await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 0, "Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SFTP Error", $"Upload failed: {localPathBefore}", 3);
                await _log.WriteLog("SFTP Exception", $"{ex.Message}", 3);
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
                    using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, bufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

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

                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 1, "Started");
                        }

                        double percent = Math.Round((double)offset / localFileSize * 100, 2);
                        if ((DateTime.Now - lastPublish).TotalMilliseconds >= ProgressReportIntervalMs &&
                            percent - lastReportedPercent >= ProgressReportThresholdPercent)
                        {
                            lastReportedPercent = percent;
                            lastPublish = DateTime.Now;

                            await PublishProgress(userId, jobId, branchID, "UploadProgress",
                                1, percent, localFileSize, offset);
                        }
                    }

                    await PublishJobResponse(userId, jobId, branchID, "UploadResponse", 2, "Completed");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Upload Attempt",
                        $"Attempt {attempt}/{MaxRetries} failed: {ex.Message}", 2);

                    if (attempt >= MaxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(RetryDelayMs * attempt, cancellationToken);

                    try
                    {
                        if (sftp.IsConnected)
                            sftp.Disconnect();
                        sftp.Dispose();
                    }
                    catch { }

                    sftp = await GetConnectionAsync(localFileSize);
                }
            }
        }

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
                await _log.WriteLog("MQTT Publish Error", $"Failed to publish {responseType}: {ex.Message}", 3);
            }
        }

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

                await _mqtt.PublishToServer(progressObj, $"server/{branchID}/SFTP/{progressType}",
                    MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Progress Error", $"Failed to publish progress: {ex.Message}", 3);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _poolLock?.Dispose();

            while (_connectionPool.TryTake(out var connInfo))
            {
                try
                {
                    if (connInfo.Client.IsConnected)
                        connInfo.Client.Disconnect();
                    connInfo.Client.Dispose();
                }
                catch { }
            }
        }
    }
}