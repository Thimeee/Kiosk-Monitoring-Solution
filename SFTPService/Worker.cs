using System.Diagnostics;
using System.Text.Json;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;
using MQTTnet.Protocol;
using SFTPService.Service;
//using SFTPService.Services;

namespace SFTPService
{
    public class Worker : BackgroundService
    {
        private readonly SftpFileService _sftp;
        private readonly MQTTHelper _mqtt;
        private readonly AppConfig _config;
        private readonly LoggerService _log;
        private readonly IPerformanceService _performance;
        private CancellationTokenSource _healthLoopCts;
        private readonly SemaphoreSlim _healthLoopLock = new SemaphoreSlim(1, 1);
        private readonly IPatchService _patchService;
        private readonly CDKApplctionStatusService _CDKApplcitionStatus;
        private Task? _cdkLoopTask;
        private readonly SqliteService _sqliteService;

        public Worker(
            SftpFileService sftpMonitor,
            MQTTHelper mqtt,
            IOptions<AppConfig> config,
            LoggerService log,
            IPerformanceService performance,
            IPatchService patchService,
            CDKApplctionStatusService cDKApplcitionStatus,
            SqliteService sqliteService
           )
        {
            _sftp = sftpMonitor;
            _mqtt = mqtt;
            _config = config.Value;
            _log = log;
            _performance = performance;
            _patchService = patchService;
            _CDKApplcitionStatus = cDKApplcitionStatus;
            _sqliteService = sqliteService;
        }

        protected override async Task<Task> ExecuteAsync(CancellationToken stoppingToken)
        {
            var mqttHost = _config.MQTT.Host;
            var mqttPort = _config.MQTT.Port;
            var mqttUserName = _config.MQTT.Username;
            var mqttPassword = _config.MQTT.Password;
            var branchId = _config.BranchId;

            _mqtt.OnReconnectedMQTT += async () =>
            {
                await _log.WriteLogAsync(LogType.Delay, "INFO:MQTT-Reconnected", "Reconnected → Re-subscribing...");

                //Send MQTT ReOnline Status
                await _mqtt.PublishAsync(
        $"server/{branchId}/STATUS/MQTTStatus",
        MQTTConnectionStatus.ONLINE,
        MqttQualityOfServiceLevel.AtLeastOnce,
        stoppingToken,
        true
    );
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
                    await _log.WriteLogAsync(LogType.Delay, "INFO:MQTT-Init", "Service Initial MQTT Connection Successfully");

                    await ServiceFirstInilize(branchId, stoppingToken);

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
                //await _log.WriteLogAsync(LogType.Delay, "INFO:MQTT-Init", "Subscribing to branch topics");s

                var branchTopic = $"branch/{branchId}/#";
                //await ServiceFirstInilize(branchId, stoppingToken);

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
                            await _log.WriteLogAsync(LogType.Delay, "ERROR:MQTT-HandlerError", "Topic Subscribing Error Please Check Connction Or (Excepthion Log)");
                            await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-HandlerError", $"Topic: {topic}, Error: {ex}");

                        }
                    }, stoppingToken);
                });

                await _log.WriteLogAsync(LogType.Delay, "INFO:MQTT-", "Branch subscription completed");

            }
            catch (TaskCanceledException)
            {
                await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-HandlerError", "Service stopping");

            }
        }

        private async Task ServiceFirstInilize(string branchId, CancellationToken stoppingToken)
        {

            //Send MQTTService restart 
            await _mqtt.PublishAsync(
    $"server/{branchId}/STATUS/ServiceStatus",
   ServiceConnectionStatus.ONLINE,
    MqttQualityOfServiceLevel.AtLeastOnce,
    stoppingToken,
    true
);
            if (!await _CDKApplcitionStatus.TestConnectionAsync())
            {
                await _mqtt.PublishAsync(
$"server/{branchId}/STATUS/DBStatus",
DBConnectionStatus.DISCONNCTED,
MqttQualityOfServiceLevel.AtLeastOnce,
stoppingToken
);
            }
            else
            {
                await _mqtt.PublishAsync(
$"server/{branchId}/STATUS/DBStatus",
DBConnectionStatus.CONNECTED,
MqttQualityOfServiceLevel.AtLeastOnce,
stoppingToken
);

                _cdkLoopTask = Task.Run(() => CDKStatusMonitoringLoop(branchId, stoppingToken), stoppingToken);
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
                else if (topic.Contains("/PATCH/Application")) // ✅ NEW
                {
                    await HandlePatchApplication(payload, branchId, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-MQTT-Handler", $"Error ProcessMessage Worker");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-MQTT-Handler", $"{ex}");
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
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-FolderStructure", $"Error FolderStructure Worker");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-FolderStructure", $"{ex}");
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
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Upload", $"Error Uplode file Worker");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Upload", $"{ex}");
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
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Download", $"Error Download file Worker");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Download", $"{ex}");
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
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Delete", $"Error Delete file Worker");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Delete", $"{ex}");
            }
        }

        private async Task CDKStatusMonitoringLoop(string branchId, CancellationToken stoppingToken)
        {
            const int maxMqttAttemptsPerStatus = 5;
            const int sendCooldownMs = 5 * 60 * 1000; // 5 minutes

            var attemptsPerStatus = new Dictionary<CDKErrorStatus, (int count, DateTime lastSent)>();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var list = await _CDKApplcitionStatus.GetAllBranchStatusAsync();

                    foreach (var item in list)
                    {
                        if (!attemptsPerStatus.TryGetValue(item.IsMaintenanceMood, out var record))
                        {
                            record = (0, DateTime.MinValue);
                        }

                        bool cooldownPassed = (DateTime.Now - record.lastSent).TotalMilliseconds > sendCooldownMs;

                        if (record.count < maxMqttAttemptsPerStatus && cooldownPassed)
                        {
                            string statusString = ((int)item.IsMaintenanceMood).ToString();

                            await _mqtt.PublishAsync(
                                $"server/{branchId}/STATUS/CDKErrorStatus",
                                statusString,
                                MqttQualityOfServiceLevel.AtLeastOnce, // safer
                                stoppingToken,
                                true);

                            attemptsPerStatus[item.IsMaintenanceMood] = (record.count + 1, DateTime.Now);

                            //await _log.WriteLog("CDKStatus",
                            //    $"Sent MQTT ({record.count + 1}/{maxMqttAttemptsPerStatus}): {statusString}", 1);
                        }
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:CDKStatus", $"Error CDKStatus Push");
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-CDKStatus", $"Error CDKStatus Push");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-CDKStatus", $"{ex}");
            }
        }


        private async Task HandleHealthRequest(string branchId, CancellationToken stoppingToken)
        {
            // Prevent race condition with lock
            await _healthLoopLock.WaitAsync(stoppingToken);
            //await _log.WriteLog("Health Loop ", "start helth");

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
                        await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Health-Loop", $"Error CDK Health ");
                        await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Health-Loop", $"{ex}");
                    }
                }, token);
            }
            finally
            {
                _healthLoopLock.Release();
            }
        }

        private async Task HandlePatchApplication(string payload, string branchId, CancellationToken stoppingToken)
        {
            try
            {
                var request = JsonSerializer.Deserialize<PatchDeploymentMqttRequest>(payload);
                if (request == null)
                {
                    await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Patch-Handler", "Invalid payload received");

                    return;
                }


                // Execute patch deployment
                bool success = await _patchService.ApplyPatchAsync(request, branchId, stoppingToken);

                if (success)
                {
                    await _log.WriteLogAsync(LogType.Delay, "SUCCES:Worker-Patch-Handler", $"Patch applied successfully - JobId: {request.PatchId}");


                }
                else
                {
                    await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Patch-Handler", $"Patch failed - JobId: {request.PatchId}");

                }
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Patch-Handler", $"Error Patch Update ");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Patch-Handler", $"{ex}");
            }
        }

        private async Task RetryConnectionAsync(int attempt, int maxRetries, int retryDelayMs, CancellationToken stoppingToken)
        {
            await _log.WriteLogAsync(LogType.Connection, "WRN:Worker-Retry-Connection", $"Initialization failed. Attempt {attempt}");


            if (attempt >= maxRetries)
            {
                await _log.WriteLogAsync(LogType.Delay, "WRN:Worker-Retry-Connection", "Max retries reached");

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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {

                // top health loop
                _healthLoopCts?.Cancel();
                _healthLoopCts?.Dispose();
                _healthLoopLock?.Dispose();

                // Gracefully shutdown MQTT
                await _mqtt.ShutdownAsync();

                if (_cdkLoopTask != null)
                {
                    await Task.WhenAny(_cdkLoopTask, Task.Delay(5000, cancellationToken)); // wait max 5s
                }

                await _log.WriteLogAsync(LogType.Delay, "SUCCES:Worker-Stop", "Service stopped cleanly");

            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:Worker-Stop", $"Error Service stopped ");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:Worker-Stop", $"Stop error: {ex}");
            }

            await base.StopAsync(cancellationToken);
        }
    }
}