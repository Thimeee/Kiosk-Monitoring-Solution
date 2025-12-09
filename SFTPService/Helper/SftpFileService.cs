using System;
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
    public class SftpFileService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _user;
        private readonly string _pass;
        private readonly LoggerService _log;
        private readonly MQTTHelper _mqtt;

        private const int BufferSize = 1024 * 1024 * 10; // 4MB
        private const int MaxRetries = 5;

        public SftpFileService(IConfiguration config, LoggerService log, MQTTHelper mqtt)
        {
            _host = config["Sftp:Host"] ?? "MAIN_SERVER_IP";
            _user = config["Sftp:Username"] ?? "sftp_user";
            _pass = config["Sftp:Password"] ?? "password";
            _port = int.TryParse(config["Sftp:Port"], out var p) ? p : 22;
            _log = log;
            _mqtt = mqtt;
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
            int attempt = 0;
            bool success = false;

            var remotePath = ConvertRealPath(remotePathBefore);
            var localPath = ConvertRealPath(localPathBefore);

            while (attempt < MaxRetries && !success)
            {
                attempt++;
                try
                {
                    using var sftp = new SftpClient(_host, _port, _user, _pass);
                    sftp.Connect();

                    var remoteFileSize = sftp.GetAttributes(remotePath).Size;
                    long offset = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;

                    if (offset > remoteFileSize) offset = 0;
                    if (offset == remoteFileSize)
                    {
                        await _mqtt.PublishToServer(new BranchJobResponse<JobDownloadResponse>
                        {
                            jobUser = userId,
                            jobEndTime = DateTime.Now,
                            jobId = jobId,
                            jobRsValue = new JobDownloadResponse { jobMsg = "AlreadyDownloaded", jobStatus = 0 }
                        }, $"server/{branchID}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce, cancellationToken);
                        return;
                    }

                    using var fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, BufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    double lastReportedPercent = 0;

                    using var remoteStream = sftp.OpenRead(remotePath);
                    remoteStream.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;

                    while (offset < remoteFileSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int chunkAttempt = 0;
                        bool chunkSuccess = false;

                        while (chunkAttempt < MaxRetries && !chunkSuccess)
                        {
                            chunkAttempt++;
                            try
                            {
                                bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                                if (bytesRead <= 0) break;

                                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                                offset += bytesRead;
                                chunkSuccess = true;

                                // Report progress
                                double percent = Math.Round((double)offset / remoteFileSize * 100, 2);
                                if (percent - lastReportedPercent >= 2)
                                {
                                    lastReportedPercent = percent;
                                    //progress?.Report(percent);

                                    var progressObj = new BranchJobResponse<JobDownloadResponse>
                                    {
                                        jobUser = userId,
                                        jobEndTime = DateTime.Now,
                                        jobId = jobId,
                                        jobRsValue = new JobDownloadResponse
                                        {
                                            jobStatus = 1,
                                            jobProgress = percent,
                                            jobTotalBytes = remoteFileSize,
                                            jobDownloadedBytes = offset
                                        }
                                    };

                                    await _mqtt.PublishToServer(progressObj, $"server/{branchID}/SFTP/DownloadProgress", MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                if (chunkAttempt >= MaxRetries) throw;
                                await Task.Delay(500, cancellationToken); // retry delay
                            }
                        }
                    }

                    sftp.Disconnect();
                    success = true;
                    await _log.WriteLog("SFTP Success", $"Downloaded '{remotePath}' to '{localPath}' successfully.");
                }
                catch (OperationCanceledException)
                {
                    await _log.WriteLog("SFTP Download", "Download cancelled by user.");
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Error", $"Download attempt {attempt} failed for {remotePathBefore}");
                    await _log.WriteLog("SFTP Exception", ex.ToString(), 3);
                    if (attempt >= MaxRetries)
                    {
                        await _mqtt.PublishToServer(new BranchJobResponse<JobDownloadResponse>
                        {
                            jobUser = userId,
                            jobEndTime = DateTime.Now,
                            jobId = jobId,
                            jobRsValue = new JobDownloadResponse
                            {
                                jobStatus = 0
                            }
                        }, $"server/{branchID}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce, cancellationToken);

                        return;
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
            int attempt = 0;
            bool success = false;

            var localPath = ConvertRealPath(localPathBefore);
            var remotePath = ConvertRealPath(remotePathBefore);

            while (attempt < MaxRetries && !success)
            {
                attempt++;
                try
                {
                    using var sftp = new SftpClient(_host, _port, _user, _pass);
                    sftp.Connect();

                    long localFileSize = new FileInfo(localPath).Length;
                    long offset = sftp.Exists(remotePath) ? sftp.GetAttributes(remotePath).Size : 0;
                    if (offset > localFileSize) offset = 0;

                    if (offset == localFileSize)
                    {
                        await _mqtt.PublishToServer(new BranchJobResponse<JobDownloadResponse>
                        {
                            jobUser = userId,
                            jobEndTime = DateTime.Now,
                            jobId = jobId,
                            jobRsValue = new JobDownloadResponse { jobMsg = "AlreadyUploaded", jobStatus = 0 }
                        }, $"server/{branchID}/SFTP/UploadResponse", MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);
                        return;
                    }

                    using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    using var remoteStream = sftp.Open(remotePath, FileMode.OpenOrCreate, FileAccess.Write);
                    remoteStream.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    double lastReportedPercent = 0;

                    while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int chunkAttempt = 0;
                        bool chunkSuccess = false;

                        while (chunkAttempt < MaxRetries && !chunkSuccess)
                        {
                            chunkAttempt++;
                            try
                            {
                                await remoteStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                                offset += bytesRead;
                                chunkSuccess = true;

                                double percent = Math.Round((double)offset / localFileSize * 100, 2);
                                if (percent - lastReportedPercent >= 2)
                                {
                                    lastReportedPercent = percent;
                                    //progress?.Report(percent);

                                    var progressObj = new BranchJobResponse<JobDownloadResponse>
                                    {
                                        jobUser = userId,
                                        jobEndTime = DateTime.Now,
                                        jobId = jobId,
                                        jobRsValue = new JobDownloadResponse
                                        {
                                            jobStatus = 1,
                                            jobProgress = percent,
                                            jobTotalBytes = localFileSize,
                                            jobDownloadedBytes = offset
                                        }
                                    };

                                    await _mqtt.PublishToServer(progressObj, $"server/{branchID}/SFTP/UploadProgress", MqttQualityOfServiceLevel.AtMostOnce, cancellationToken);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                if (chunkAttempt >= MaxRetries) throw;
                                await Task.Delay(500, cancellationToken); // retry delay
                            }
                        }
                    }

                    sftp.Disconnect();
                    success = true;
                    await _log.WriteLog("SFTP Success", $"Uploaded '{localPath}' to '{remotePath}' successfully.");
                }
                catch (OperationCanceledException)
                {
                    await _log.WriteLog("SFTP Upload", "Upload cancelled by user.");
                    throw;
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Error", $"Upload attempt {attempt} failed for {localPathBefore}");
                    await _log.WriteLog("SFTP Exception", ex.ToString(), 3);
                    if (attempt >= MaxRetries)
                    {
                        await _mqtt.PublishToServer(new BranchJobResponse<JobDownloadResponse>
                        {
                            jobUser = userId,
                            jobEndTime = DateTime.Now,
                            jobId = jobId,
                            jobRsValue = new JobDownloadResponse
                            {
                                jobStatus = 0
                            }
                        }, $"server/{branchID}/SFTP/UploadResponse", MqttQualityOfServiceLevel.ExactlyOnce, cancellationToken);

                        return;
                    }
                }
            }
        }



    }

}
