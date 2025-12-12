using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MQTTnet;
using MQTTnet.Protocol;
using SFTPService.Helper;
using static System.Net.WebRequestMethods;

namespace SFTPService
{
    public class Worker : BackgroundService
    {
        private readonly SftpFileService _sftp;
        //private readonly RabbitHelper _rabbit;
        private readonly MQTTHelper _mqtt;
        private readonly IConfiguration _config;
        private readonly LoggerService _log;
        //private IMqttClient _mqttClient;
        private readonly IPerformanceService _performance;
        public Worker(
            SftpFileService sftpMonitor,
            //RabbitHelper rabbitMq,
            MQTTHelper mqtt,
            IConfiguration config,
            LoggerService log,
            IPerformanceService performance)
        {
            _sftp = sftpMonitor;
            //_rabbit = rabbitMq;
            _mqtt = mqtt;
            _config = config;
            _log = log;
            _performance = performance;



        }
        protected override async Task<Task> ExecuteAsync(CancellationToken stoppingToken)
        {
            bool MQTTInit = false;
            var MQTTHost = _config["MQTT:Host"] ?? "localhost";
            var MQTTPort = _config["MQTT:Port"] ?? "1883";
            var MQTTUserName = _config["MQTT:Username"] ?? "User";
            var MQTTPW = _config["MQTT:Password"] ?? "1234";
            var branchId = _config["BranchId"] ?? "BR001";
            int MQTTPortInt = int.TryParse(MQTTPort, out var port) ? port : 1883;

            _mqtt.OnReconnectedMQTT += async () =>
            {
                await _log.WriteLog("Worker", "Reconnected → Re-subscribing queues...");
                await StartMQTTEvent(branchId, stoppingToken);
            };

            int attempt = 0;
            const int maxRetries = 100;
            const int retryDelayMs = 3000;



            while (!MQTTInit && !stoppingToken.IsCancellationRequested)
            {
                await _log.WriteLog("MQTT Init ", $"MQTTHelperInit try");

                //MQTTInit = await _mqtt.InitAsync("127.0.0.1", "MQTTUser", "Thimi@1234", 1883);
                MQTTInit = await _mqtt.InitAsync(MQTTHost, MQTTUserName, MQTTPW, MQTTPortInt);
                await _log.WriteLog("MQTT Init ", $"MQTTHelperInit Oky");

                if (!MQTTInit)
                {
                    attempt++;
                    await RetryConnectionAsync(attempt, maxRetries, retryDelayMs, stoppingToken);

                }
                else
                {
                    await StartMQTTEvent(branchId, stoppingToken);

                }

            }



            return Task.CompletedTask;


        }

        private CancellationTokenSource _perfLoopCts;
        private async Task StartMQTTEvent(string branchId, CancellationToken stoppingToken)
        {
            try
            {

                await _log.WriteLog("MQTT Init ", $"MQTTHelperInit Oky 2");


                var branchTopic = $"branch/{branchId}/#";

                //Subscribe Event

                await _mqtt.SubscribeAsync(branchTopic, async (payload, topic) =>
                {
                    //await _log.WriteLog("MQTT", $"SubscribeBranchQueue SFTP");

                    try
                    {

                        if (topic.Contains("/SFTP/"))
                        {
                            if (topic.EndsWith("/FolderStucher"))
                            {
                                //FolderStucherResponse
                                await _log.WriteLog("MQTT SFTPFolderStucher", $"{payload}");

                                try
                                {
                                    var job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);

                                    if (job.jobRqValue != null)
                                    {
                                        if (job.jobRqValue.branch != null)
                                        {
                                            var folder = new GetFolderStructure();
                                            var rootNode = await folder.GetFolderStructureRootAsync($"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}");



                                            if (rootNode != null)
                                            {
                                                var resObj = new BranchJobResponse<FolderNode>
                                                {
                                                    jobUser = job.jobUser,
                                                    jobEndTime = DateTime.Now,
                                                    jobRsValue = rootNode
                                                };
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/FolderStucherResponse", MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                                                await _log.WriteLog("UploadFileToMain", "Folder structure sent successfully.");
                                            }



                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    await _log.WriteLog("Service Error SFTP (FolderStucher)", $"Service Error Check ExecptionLog -> (Service Exception) ");
                                    await _log.WriteLog("Service Exception (SubscribeBranchQueue) SFTP (FolderStucher)", $"Unexpected error: {ex}", 3);
                                }

                            }
                            else if (topic.EndsWith("/Upload"))
                            {
                                try
                                {
                                    var job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);

                                    //await _log.WriteLog("MQTT SFTPupload", $"{payload}");
                                    if (job.jobRqValue != null)
                                    {
                                        if (job.jobRqValue.server != null && job.jobRqValue.branch != null)
                                        {
                                            await _log.WriteLog("UploadFileToMain ", $"UploadFileToMain Try");

                                            //string remoteFile = "/upload/noVNC-1.6.0.zip"; // NOT C:/...
                                            await _sftp.UploadFileAsync($"{job.jobRqValue.branch.path}", $"{job.jobRqValue.server.path}/{job.jobRqValue.server.name}", job.jobUser, branchId, job.jobId);

                                            await _log.WriteLog("UploadFileToMain ", $"UploadFileToMain Done");

                                            var resObj = new BranchJobResponse<JobDownloadResponse>
                                            {
                                                jobUser = job.jobUser,
                                                jobEndTime = DateTime.Now,
                                                jobId = job.jobId,
                                                jobRsValue = new JobDownloadResponse
                                                {
                                                    jobStatus = 2
                                                }
                                            };
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/UploadResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken); ;

                                            await _log.WriteLog("UploadFileToMain ", $"all oky");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    await _log.WriteLog("Service Error SFTP (Upload)", $"Service Error Check ExecptionLog -> (Service Exception) ");
                                    await _log.WriteLog("Service Exception (SubscribeBranchQueue) SFTP (Upload)", $"Unexpected error: {ex}", 3);
                                }

                            }
                            else if (topic.EndsWith("/Download"))
                            {
                                BranchJobRequest<FileDetails>? job = null;
                                BranchJobResponse<JobDownloadResponse>? resObj = null;
                                try
                                {
                                    job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);

                                    if (job != null)
                                    {
                                        if (job.jobRqValue != null)
                                        {
                                            if (job.jobRqValue.server != null && job.jobRqValue.branch != null)
                                            {
                                                var progress = new Progress<double>(p =>
                                                {
                                                    //if want write log
                                                    //Console.WriteLine($"Download Progress: {p}%");
                                                });


                                                await _sftp.DownloadFileAsync($"{job.jobRqValue.server.path}", $"{job.jobRqValue.branch.path}/{job.jobRqValue.server.name}", job.jobUser, branchId, job.jobId, progress);

                                                await _log.WriteLog("DownloadGlobalUpdate ", $"DownloadGlobalUpdate Done");

                                                resObj = new BranchJobResponse<JobDownloadResponse>
                                                {
                                                    jobUser = job.jobUser,
                                                    jobEndTime = DateTime.Now,
                                                    jobId = job.jobId,
                                                    jobRsValue = new JobDownloadResponse
                                                    {
                                                        jobStatus = 2
                                                    }
                                                };
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);

                                                await _log.WriteLog("DownloadGlobalUpdate ", $"Send RabbitMq Status Done DownloadGlobalUpdate");
                                            }
                                        }
                                    }


                                }
                                catch (Exception ex)
                                {
                                    if (job != null && job.jobRqValue != null)
                                    {
                                        if (resObj != null && resObj.jobRsValue != null)
                                        {
                                            resObj.jobRsValue.jobStatus = 0;
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
                                        }

                                    }
                                    await _log.WriteLog("Service Error SFTP (Download)", $"Service Error Check ExecptionLog -> (Service Exception) ");
                                    await _log.WriteLog("Service Exception (SubscribeBranchQueue) SFTP (Download)", $"Unexpected error: {ex}", 3);
                                }
                            }
                            else if (topic.EndsWith("/Delete"))
                            {
                                BranchJobRequest<FileDetails>? job = null;
                                BranchJobResponse<JobDownloadResponse>? resObj = null;
                                try
                                {
                                    job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(payload);


                                    if (job.jobRqValue != null)
                                    {
                                        if (job.jobRqValue.branch != null)
                                        {
                                            if (job.jobRqValue.branch.path != null)
                                            {

                                                var filePath = _sftp.ConvertRealPath(job.jobRqValue.branch.path);

                                                resObj = new BranchJobResponse<JobDownloadResponse>
                                                {
                                                    jobUser = job.jobUser,
                                                    jobId = job.jobId,
                                                    jobEndTime = DateTime.Now,
                                                    jobRsValue = new JobDownloadResponse()
                                                };


                                                resObj.jobRsValue.jobStatus = 1;
                                                resObj.jobRsValue.jobProgress = 10;
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);
                                                await Task.Delay(100, stoppingToken);


                                                if (!System.IO.File.Exists(filePath))
                                                {
                                                    await _log.WriteLog("DeleteFile ", $"File not found");
                                                    resObj.jobRsValue.jobStatus = 0;
                                                    await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
                                                    return;
                                                }

                                                resObj.jobRsValue.jobStatus = 1;
                                                resObj.jobRsValue.jobProgress = 60;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);
                                                await Task.Delay(100, stoppingToken);

                                                System.IO.File.Delete(filePath);

                                                resObj.jobRsValue.jobStatus = 1;
                                                resObj.jobRsValue.jobProgress = 100;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                                                await _log.WriteLog("DeleteFile ", $"DownloadGlobalUpdate Done");

                                                await Task.Delay(300, stoppingToken);

                                                resObj.jobRsValue.jobStatus = 2;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);

                                                await _log.WriteLog("DownloadGlobalUpdate ", $"Send RabbitMq Status Done DownloadGlobalUpdate");
                                            }

                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (job != null && job.jobRqValue != null)
                                    {
                                        if (resObj != null && resObj.jobRsValue != null)
                                        {
                                            resObj.jobRsValue.jobStatus = 0;
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce, stoppingToken);
                                        }

                                    }

                                    await _log.WriteLog("Service Error SFTP (Delete)", $"Service Error Check ExecptionLog -> (Service Exception) ");
                                    await _log.WriteLog("Service Exception (SubscribeBranchQueue) SFTP (Delete)", $"Unexpected error: {ex}", 3);
                                }


                            }


                            //return;
                        }
                        else if (topic.Contains("/HEALTH/PerformanceReq"))
                        {
                            await _log.WriteLog("sendhelth", "start before loop");

                            // Cancel previous loop if running
                            _perfLoopCts?.Cancel();
                            _perfLoopCts = new CancellationTokenSource();
                            var token = _perfLoopCts.Token;

                            _ = Task.Run(async () =>
                            {
                                var loopStart = DateTime.Now;

                                try
                                {
                                    while (!token.IsCancellationRequested)
                                    {
                                        // Stop automatically after 10 minutes
                                        if ((DateTime.Now - loopStart).TotalMinutes >= 2)
                                            break;

                                        var perf = await _performance.GetPerformanceAsync();

                                        var resObj = new BranchJobResponse<PerformanceInfo>
                                        {
                                            jobEndTime = DateTime.Now,
                                            jobRsValue = perf
                                        };

                                        await _mqtt.PublishToServer(resObj, $"server/{branchId}/HEALTH/PerformanceRespo", MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

                                        // Wait 1 second (or whatever interval you want)
                                        await Task.Delay(1000, token);
                                    }

                                    await _log.WriteLog("sendhelth", "Send health loop ended (auto or canceled)");
                                }
                                catch (TaskCanceledException)
                                {
                                    await _log.WriteLog("sendhelth", "Send health loop canceled by new request");
                                }
                            });
                        }



                    }
                    catch (Exception ex)
                    {
                        //await SendStatusBranch($"Error: {ex.Message}", job.file.name, _rabbit);
                        await _log.WriteLog("Service Error MQTT SFTP", $"Service Error Check ExecptionLog -> (Service Exception) ");
                        await _log.WriteLog("Service Exception (SubscribeBranchQueue) MQTT SFTP", $"Unexpected error: {ex}", 3);
                        await _log.WriteLog("Service Error (DealyLog/ExecptionLog) MQTT SFTP", "Service Error ", 2);

                    }
                });


            }
            catch (TaskCanceledException)
            {
                // Service is stopping, exit
            }
        }






        private async Task RetryConnectionAsync(int attempt, int maxRetries, int retryDelayMs, CancellationToken stoppingToken)
        {



            await _log.WriteLog("Service Warning (DealyLog/ExecptionLog)", $"Initialization failed. Attempt {attempt}/{maxRetries}", 2);

            if (attempt >= maxRetries)
            {
                await _log.WriteLog("Service Error (DealyLog/ExecptionLog)", "Max retries reached. Service cannot continue.", 2);
                //return;
                //attempt = 0;
            }
            try
            {
                await Task.Delay(retryDelayMs, stoppingToken); // wait before retry
            }
            catch (TaskCanceledException)
            {
                // Service is stopping, exit
                return;
            }
        }





    }



}

