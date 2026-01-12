using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Monitoring.Shared.DTO;
using MQTTnet.Protocol;
using SFTPService.Helper;

namespace SFTPService
{
    public class Worker : BackgroundService
    {
        private readonly SftpFileService _sftp;
        private readonly MQTTHelper _mqtt;
        private readonly IConfiguration _config;
        private readonly LoggerService _log;
        private readonly IPerformanceService _performance;
        private CancellationTokenSource _healthLoopCts;
        private readonly SemaphoreSlim _healthLoopLock = new SemaphoreSlim(1, 1);

        public Worker(
            SftpFileService sftpMonitor,
            MQTTHelper mqtt,
            IConfiguration config,
            LoggerService log,
            IPerformanceService performance)
        {
            _sftp = sftpMonitor;
            _mqtt = mqtt;
            _config = config;
            _log = log;
            _performance = performance;
        }

        protected override async Task<Task> ExecuteAsync(CancellationToken stoppingToken)
        {
            var mqttHost = _config["MQTT:Host"] ?? "localhost";
            var mqttPort = int.TryParse(_config["MQTT:Port"], out var port) ? port : 1883;
            var mqttUserName = _config["MQTT:Username"] ?? "User";
            var mqttPassword = _config["MQTT:Password"] ?? "1234";
            var branchId = _config["BranchId"] ?? "BR001";

            _mqtt.OnReconnectedMQTT += async () =>
            {
                await _log.WriteLog("Worker", "Reconnected → Re-subscribing...");
                await StartMQTTEvent(branchId, stoppingToken);
            };

            await InitializeMqttConnection(mqttHost, mqttUserName, mqttPassword, mqttPort, branchId, stoppingToken);

            return Task.CompletedTask;
        }

        private async Task InitializeMqttConnection(
            string host,
            string username,
            string password,
            int port,
            string branchId,
            CancellationToken stoppingToken)
        {
            int attempt = 0;
            const int maxRetries = 100;
            const int retryDelayMs = 3000;

            while (!stoppingToken.IsCancellationRequested)
            {
                bool connected = await _mqtt.InitAsync(host, username, password, port);

                if (connected)
                {
                    await _log.WriteLog("MQTT Init", "Connection successful");
                    await StartMQTTEvent(branchId, stoppingToken);
                    break;
                }

                attempt++;
                await RetryConnectionAsync(attempt, maxRetries, retryDelayMs, stoppingToken);
            }
        }

        private async Task StartMQTTEvent(string branchId, CancellationToken stoppingToken)
        {
            try
            {
                await _log.WriteLog("MQTT Init", "Subscribing to branch topics");

                var branchTopic = $"branch/{branchId}/#";

                await _mqtt.SubscribeAsync(branchTopic, async (payload, topic) =>
                {
                    // Fire-and-forget with exception handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessBranchMessage(topic, payload, branchId, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            await _log.WriteLog("MQTT Handler Fatal", $"Topic: {topic}, Error: {ex.Message}", 3);
                        }
                    }, stoppingToken);
                });

                await _log.WriteLog("MQTT", "Branch subscription completed");
            }
            catch (TaskCanceledException)
            {
                await _log.WriteLog("MQTT", "Service stopping");
            }
        }

        private async Task ProcessBranchMessage(string topic, string payload, string branchId, CancellationToken stoppingToken)
        {
            try
            {
                if (topic.Contains("/SFTP/"))
                {
                    await HandleSFTPMessage(topic, payload, branchId, stoppingToken);
                }
                else if (topic.Contains("/HEALTH/PerformanceReq"))
                {
                    await HandleHealthRequest(branchId, stoppingToken);
                }
                else if (topic.Contains("/PATCH/Application"))
                {
                    await HandlePatchApplication();
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Handler Exception", $"Error: {ex.Message}", 3);
            }
        }

        private async Task HandleSFTPMessage(string topic, string payload, string branchId, CancellationToken stoppingToken)
        {
            if (topic.EndsWith("/FolderStucher"))
            {
                await HandleFolderStructure(payload, branchId, stoppingToken);
            }
            else if (topic.EndsWith("/Upload"))
            {
                await HandleUpload(payload, branchId, stoppingToken);
            }
            else if (topic.EndsWith("/Download"))
            {
                await HandleDownload(payload, branchId, stoppingToken);
            }
            else if (topic.EndsWith("/Delete"))
            {
                await HandleDelete(payload, branchId, stoppingToken);
            }
        }

        private async Task HandleFolderStructure(string payload, string branchId, CancellationToken stoppingToken)
        {
            try
            {
                var job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);
                if (job?.jobRqValue?.branch == null) return;

                var folder = new GetFolderStructure();
                var rootNode = await folder.GetFolderStructureRootAsync(
                    $"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}");

                if (rootNode != null)
                {
                    var response = new BranchJobResponse<FolderNode>
                    {
                        jobUser = job.jobUser,
                        jobEndTime = DateTime.Now,
                        jobRsValue = rootNode
                    };

                    await _mqtt.PublishToServer(
                        response,
                        $"server/{branchId}/SFTP/FolderStucherResponse",
                        MqttQualityOfServiceLevel.AtMostOnce,
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("FolderStructure Error", ex.Message, 3);
            }
        }

        private async Task HandleUpload(string payload, string branchId, CancellationToken stoppingToken)
        {
            try
            {
                var job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);
                if (job?.jobRqValue?.server == null || job.jobRqValue.branch == null) return;

                await _sftp.UploadFileAsync(
                    job.jobRqValue.branch.path,
                    $"{job.jobRqValue.server.path}/{job.jobRqValue.server.name}",
                    job.jobUser,
                    branchId,
                    job.jobId);

                var response = new BranchJobResponse<JobDownloadResponse>
                {
                    jobUser = job.jobUser,
                    jobEndTime = DateTime.Now,
                    jobId = job.jobId,
                    jobRsValue = new JobDownloadResponse { jobStatus = 2 }
                };

                await _mqtt.PublishToServer(
                    response,
                    $"server/{branchId}/SFTP/UploadResponse",
                    MqttQualityOfServiceLevel.ExactlyOnce,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Upload Error", ex.Message, 3);
            }
        }

        private async Task HandleDownload(string payload, string branchId, CancellationToken stoppingToken)
        {
            BranchJobRequest<FileDetails> job = null;
            BranchJobResponse<JobDownloadResponse> response = null;

            try
            {
                job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);
                if (job?.jobRqValue?.server == null || job.jobRqValue.branch == null) return;

                var progress = new Progress<double>();

                await _sftp.DownloadFileAsync(
                    job.jobRqValue.server.path,
                    $"{job.jobRqValue.branch.path}/{job.jobRqValue.server.name}",
                    job.jobUser,
                    branchId,
                    job.jobId,
                    progress);

                response = new BranchJobResponse<JobDownloadResponse>
                {
                    jobUser = job.jobUser,
                    jobEndTime = DateTime.Now,
                    jobId = job.jobId,
                    jobRsValue = new JobDownloadResponse { jobStatus = 2 }
                };

                await _mqtt.PublishToServer(
                    response,
                    $"server/{branchId}/SFTP/DownloadResponse",
                    MqttQualityOfServiceLevel.ExactlyOnce,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                if (response?.jobRsValue != null)
                {
                    response.jobRsValue.jobStatus = 0;
                    await _mqtt.PublishToServer(
                        response,
                        $"server/{branchId}/SFTP/DownloadResponse",
                        MqttQualityOfServiceLevel.ExactlyOnce,
                        stoppingToken);
                }
                await _log.WriteLog("Download Error", ex.Message, 3);
            }
        }

        private async Task HandleDelete(string payload, string branchId, CancellationToken stoppingToken)
        {
            BranchJobRequest<FileDetails> job = null;
            BranchJobResponse<JobDownloadResponse> response = null;

            try
            {
                job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);
                if (job?.jobRqValue?.branch?.path == null) return;

                var filePath = _sftp.ConvertRealPath(job.jobRqValue.branch.path);

                response = new BranchJobResponse<JobDownloadResponse>
                {
                    jobUser = job.jobUser,
                    jobId = job.jobId,
                    jobEndTime = DateTime.Now,
                    jobRsValue = new JobDownloadResponse { jobStatus = 1, jobProgress = 10 }
                };

                await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                    MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                if (!File.Exists(filePath))
                {
                    response.jobRsValue.jobStatus = 0;
                    await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                        MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
                    return;
                }

                response.jobRsValue.jobProgress = 60;
                await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                    MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                File.Delete(filePath);

                response.jobRsValue.jobProgress = 100;
                await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                    MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                await Task.Delay(300, stoppingToken);

                response.jobRsValue.jobStatus = 2;
                await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                    MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
            }
            catch (Exception ex)
            {
                if (response?.jobRsValue != null)
                {
                    response.jobRsValue.jobStatus = 0;
                    await _mqtt.PublishToServer(response, $"server/{branchId}/SFTP/DeleteResponse",
                        MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
                }
                await _log.WriteLog("Delete Error", ex.Message, 3);
            }
        }

        private async Task HandleHealthRequest(string branchId, CancellationToken stoppingToken)
        {
            // Prevent race condition with lock
            await _healthLoopLock.WaitAsync(stoppingToken);

            try
            {
                // Cancel and dispose previous loop
                _healthLoopCts?.Cancel();
                _healthLoopCts?.Dispose();

                _healthLoopCts = new CancellationTokenSource();
                var token = _healthLoopCts.Token;

                // Fire-and-forget with exception handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var loopStart = DateTime.Now;

                        while (!token.IsCancellationRequested)
                        {
                            if ((DateTime.Now - loopStart).TotalMinutes >= 5)
                                break;

                            var perf = await _performance.GetPerformanceAsync();

                            var response = new BranchJobResponse<PerformanceInfo>
                            {
                                jobEndTime = DateTime.Now,
                                jobRsValue = perf
                            };

                            await _mqtt.PublishToServer(
                                response,
                                $"server/{branchId}/HEALTH/PerformanceRespo",
                                MqttQualityOfServiceLevel.AtMostOnce,
                                stoppingToken);

                            await Task.Delay(1000, token);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Normal cancellation
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLog("Health Loop Error", ex.Message, 3);
                    }
                }, token);
            }
            finally
            {
                _healthLoopLock.Release();
            }
        }

        private async Task HandlePatchApplication()
        {
            try
            {
                string scriptPath = @"C:\Branches\MCS\Patches\AllNewPatches\Scripts\update.ps1";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(output))
                    await _log.WriteLog("Patch Output", output);

                if (!string.IsNullOrEmpty(error))
                    await _log.WriteLog("Patch Error", error, 3);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Application Error", ex.Message, 3);
            }
        }

        private async Task RetryConnectionAsync(int attempt, int maxRetries, int retryDelayMs, CancellationToken stoppingToken)
        {
            await _log.WriteLog("Service Warning", $"Initialization failed. Attempt {attempt}/{maxRetries}", 2);

            if (attempt >= maxRetries)
            {
                await _log.WriteLog("Service Error", "Max retries reached", 2);
                return;
            }

            try
            {
                await Task.Delay(retryDelayMs, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Service stopping
            }
        }

        public override void Dispose()
        {
            _healthLoopCts?.Cancel();
            _healthLoopCts?.Dispose();
            _healthLoopLock?.Dispose();
            base.Dispose();
        }
    }
}