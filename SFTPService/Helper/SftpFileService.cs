using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.DTO;
using MQTTnet.Protocol;
using Renci.SshNet;

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

        private const int BufferSize = 1024 * 1024 * 4; // 4MB
        private const int MaxRetries = 3;

        public SftpFileService(IConfiguration config, LoggerService log, MQTTHelper mqtt)
        {
            _host = config["Sftp:Host"] ?? "MAIN_SERVER_IP";
            _user = config["Sftp:Username"] ?? "sftp_user";
            _pass = config["Sftp:Password"] ?? "password";
            _port = int.TryParse(config["Sftp:Port"], out var p) ? p : 22;
            _log = log;
            _mqtt = mqtt;
        }


        private string ConvertRemotePath(string remotePath)
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


        public async Task DownloadFileAsync(string remotePathBefore, string localPathBefore, string userId, string branchID, IProgress<double>? progress = null)

        {
            int attempt = 0;
            bool success = false;



            while (attempt < MaxRetries && !success)
            {
                attempt++;
                try
                {
                    var remotePath = ConvertRemotePath(remotePathBefore);
                    var localPath = ConvertRemotePath(localPathBefore);

                    using var sftp = new SftpClient(_host, _port, _user, _pass);
                    sftp.Connect();

                    var remoteFileSize = sftp.GetAttributes(remotePath).Size;
                    long offset = 0;

                    // Resume partial download
                    if (File.Exists(localPath))
                    {
                        offset = new System.IO.FileInfo(localPath).Length;
                        if (offset > remoteFileSize) offset = 0;
                        if (offset == remoteFileSize)
                        {
                            var progressObj = new BranchJobResponse<JobDownloadResponse>
                            {
                                jobUser = userId,
                                jobEndTime = DateTime.Now,
                                jobRsValue = new JobDownloadResponse
                                {
                                    jobMsg = $"AllReadyDownload",
                                    jobStatus = 0,
                                }
                            };

                            await _mqtt.PublishToServer(
                                progressObj,
                                $"server/{branchID}/SFTP/DownloadResponse",
                                MqttQualityOfServiceLevel.AtLeastOnce);

                            return;
                        }
                    }

                    using var fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, BufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[BufferSize];
                    while (offset < remoteFileSize)
                    {
                        int bytesRead = sftp.DownloadFileChunk(remotePath, buffer, offset);
                        if (bytesRead <= 0) break;

                        await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                        offset += bytesRead;

                        double percent = Math.Round((double)offset / remoteFileSize * 100, 2);
                        progress?.Report(percent);

                        var progressObj = new BranchJobResponse<JobDownloadResponse>
                        {
                            jobUser = userId,
                            jobEndTime = DateTime.Now,
                            jobRsValue = new JobDownloadResponse
                            {
                                jobMsg = $"Downloading... {percent}%",
                                jobStatus = 0,
                                jobProgress = percent,
                                jobTotalBytes = remoteFileSize,
                                jobDownloadedBytes = offset
                            }
                        };

                        await _mqtt.PublishToServer(
                            progressObj,
                            $"server/{branchID}/SFTP/DownloadProgress",
                            MqttQualityOfServiceLevel.AtMostOnce);
                    }

                    sftp.Disconnect();
                    success = true;
                    await _log.WriteLog("SFTP Success", $"Downloaded '{remotePath}' to '{localPath}' successfully.");
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Error", $"Download attempt {attempt} failed for {remotePathBefore}");
                    await _log.WriteLog("SFTP Exception", ex.ToString(), 3);

                    if (attempt >= MaxRetries) throw;
                }
            }
        }

        public async Task UploadFileAsync(string localPathBefore, string remotePathBefore, IProgress<double>? progress = null)
        {
            int attempt = 0;
            bool success = false;



            while (attempt < MaxRetries && !success)
            {
                attempt++;
                try
                {
                    var remotePath = ConvertRemotePath(remotePathBefore);
                    var localPath = ConvertRemotePath(localPathBefore);

                    using var sftp = new SftpClient(_host, _port, _user, _pass);
                    sftp.Connect();

                    long localFileSize = new System.IO.FileInfo(localPath).Length;
                    long offset = 0;

                    if (sftp.Exists(remotePath))
                    {
                        offset = sftp.GetAttributes(remotePath).Size;
                        if (offset > localFileSize) offset = 0;
                    }

                    using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
                    fs.Seek(offset, SeekOrigin.Begin);

                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        using var remoteStream = sftp.Open(remotePath, FileMode.OpenOrCreate, FileAccess.Write);
                        remoteStream.Seek(offset, SeekOrigin.Begin);
                        await remoteStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        offset += bytesRead;

                        progress?.Report((double)offset / localFileSize * 100);
                    }

                    sftp.Disconnect();
                    success = true;
                    await _log.WriteLog("SFTP Success", $"Uploaded '{localPath}' to '{remotePath}' successfully.");
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("SFTP Error", $"Upload attempt {attempt} failed for {localPathBefore}");
                    await _log.WriteLog("SFTP Exception", ex.ToString(), 3);

                    if (attempt >= MaxRetries) throw;
                }
            }
        }


    }
    public static class SftpClientExtensions
    {
        public static int DownloadFileChunk(this SftpClient client, string remotePath, byte[] buffer, long offset)
        {
            using var remoteStream = client.OpenRead(remotePath);
            remoteStream.Seek(offset, SeekOrigin.Begin);
            return remoteStream.Read(buffer, 0, buffer.Length);
        }
    }
}
