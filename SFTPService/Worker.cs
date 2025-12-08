using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Monitoring.Grpc;
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
            const int maxRetries = 10;
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
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/FolderStucherResponse", MqttQualityOfServiceLevel.AtMostOnce);

                                                await _log.WriteLog("UploadFileToMain", "Folder structure sent successfully.");
                                            }

                                            //await PrintFolderNode(rootNode);

                                            //var options = new JsonSerializerOptions
                                            //{
                                            //    WriteIndented = true
                                            //};
                                            //string jsonMessage = JsonSerializer.Serialize(rootNode, options);

                                            //await _log.WriteLog("UploadFileToMain ", $"all oky");*/

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
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/UploadResponse", MqttQualityOfServiceLevel.ExactlyOnce); ;

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
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce);

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
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DownloadResponse", MqttQualityOfServiceLevel.ExactlyOnce);
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
                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce);
                                                await Task.Delay(100);


                                                if (!System.IO.File.Exists(filePath))
                                                {
                                                    await _log.WriteLog("DeleteFile ", $"File not found");
                                                    resObj.jobRsValue.jobStatus = 0;
                                                    await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce);
                                                    return;
                                                }

                                                resObj.jobRsValue.jobStatus = 1;
                                                resObj.jobRsValue.jobProgress = 60;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce);
                                                await Task.Delay(100);

                                                System.IO.File.Delete(filePath);

                                                resObj.jobRsValue.jobStatus = 1;
                                                resObj.jobRsValue.jobProgress = 100;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.AtMostOnce);

                                                await _log.WriteLog("DeleteFile ", $"DownloadGlobalUpdate Done");

                                                await Task.Delay(300);

                                                resObj.jobRsValue.jobStatus = 2;

                                                await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce);

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
                                            await _mqtt.PublishToServer(resObj, $"server/{branchId}/SFTP/DeleteResponse", MqttQualityOfServiceLevel.ExactlyOnce);
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

                                        await _mqtt.PublishToServer(resObj, $"server/{branchId}/HEALTH/PerformanceRespo", MqttQualityOfServiceLevel.AtMostOnce);

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

                //Subscribe Event


                //while (!stoppingToken.IsCancellationRequested)
                //{
                //    var obj = new BranchCommand
                //    {
                //        BranchId = branchId,
                //        Action = "Online",
                //        Payload = DateTime.UtcNow.ToString("o")
                //    };

                //    var json = JsonSerializer.Serialize(obj);


                //    await _mqtt.PublishToServer(json, $"branch/BRANCH001/SFTP/FolderStucher", MqttQualityOfServiceLevel.AtLeastOnce);
                //    await _log.WriteLog("MQTT", $"Heartbeat sent for branch {branchId}");

                //    await Task.Delay(5000, stoppingToken); // send every 5 seconds
                //}

            }
            catch (TaskCanceledException)
            {
                // Service is stopping, exit
            }
        }




        //bool rabbitInit = false;
        //var host = _config["RabbitMQ:Host"] ?? "01";
        //var Port = _config["RabbitMQ:Port"] ?? "01";
        //var UserName = _config["RabbitMQ:Username"] ?? "01";
        //var Pw = _config["RabbitMQ:Password"] ?? "01";

        //await gRPC(stoppingToken);
        //_rabbit.OnReconnected += async () =>
        //{
        //    await _log.WriteLog("Worker", "Reconnected → Re-subscribing queues...");
        //    await StartRabbitMQEvents(branchId, stoppingToken);
        //};


        //int attempt = 0;
        //const int maxRetries = 10;
        //const int retryDelayMs = 3000;



        //while (!rabbitInit && !stoppingToken.IsCancellationRequested)
        //{
        //    rabbitInit = await _rabbit.RabbitHelperInit(host, UserName, Pw);

        //    if (!rabbitInit)
        //    {
        //        attempt++;
        //        await RetryConnectionAsync(attempt, maxRetries, retryDelayMs, stoppingToken);

        //    }
        //    else
        //    {
        //        await StartRabbitMQEvents(branchId, stoppingToken);

        //    }

        //}
        //await _log.WriteLog("project start", $"main start Oky");

        private async Task<IMqttClient> ConnectToMQTT(string branchId, string Host, int Port, string User, string Pw, CancellationToken stoppingToken)
        {
            try
            {
                var factory = new MqttClientFactory();
                var mqttClient = factory.CreateMqttClient();

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(Host, 1883)
                    .WithCredentials(User, Pw) // if required
                    .Build();

                mqttClient.ConnectedAsync += async e =>
                {
                    await _log.WriteLog("MQTT", $"Branch {branchId} connected to broker!");

                    // Subscribe to own topics
                    await mqttClient.SubscribeAsync($"branch/{branchId}/#");
                    await mqttClient.SubscribeAsync("branch/all/notification");

                    await _log.WriteLog("MQTT", $"Subscribed to own topics + broadcast");

                };
                // Message received event
                mqttClient.ApplicationMessageReceivedAsync += async e =>
                {
                    string topic = e.ApplicationMessage.Topic;
                    string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                    if (topic == $"branch/all/notification")
                        await _log.WriteLog("MQTT", $"[Broadcast] {payload}");

                    //else
                    //    await _log.WriteLog("MQTT", $"[Branch Message] Topic={topic}, Payload={payload}");


                    //await _log.WriteLog("MQTT", $"Message received: Topic={topic}, Payload={payload}");
                };


                mqttClient.DisconnectedAsync += async e =>
                {
                    await _log.WriteLog("MQTT", $"Disconnected from broker, reconnecting...");

                    await Task.Delay(5000); // wait 5 sec before reconnect
                    try
                    {
                        await mqttClient.ConnectAsync(options, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLog("MQTT", $"Reconnect failed: {ex.Message}");

                    }
                };


                await mqttClient.ConnectAsync(options);
                await _log.WriteLog("MQTT", $"Connected to local MQTT broker");

                return mqttClient;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT", $"Error during MQTT connection setup: {ex.Message}");
                return null;
            }

        }

        private async Task gRPC(CancellationToken stoppingToken)
        {
            // Use SocketsHttpHandler to support HTTP/2 over plain HTTP only use http
            var httpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            };

            //https use this
            //using var channel = GrpcChannel.ForAddress("https://monitoring.bank.lk:5155", new GrpcChannelOptions
            //{
            //    Credentials = Grpc.Core.ChannelCredentials.SecureSsl
            //});
            //var client = new BranchMonitor.BranchMonitorClient(channel);


            using var channel = GrpcChannel.ForAddress("http://localhost:5155", new GrpcChannelOptions
            {
                HttpHandler = httpHandler,
                Credentials = Grpc.Core.ChannelCredentials.Insecure
            });

            var client = new BranchMonitor.BranchMonitorClient(channel);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var heartbeat = new HeartbeatRequest
                    {
                        BranchId = "BRANCH001",
                        ActiveUsers = 5,
                        Timestamp = DateTime.UtcNow.Ticks
                    };

                    var reply = await client.HeartbeatAsync(heartbeat, cancellationToken: stoppingToken);

                    if (reply.Success)
                        await _log.WriteLog("gRPC", $"Heartbeat sent successfully: {reply.Message}");
                    else
                        await _log.WriteLog("gRPC", "Heartbeat failed.");
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("gRPC", "Error sending heartbeat");
                    await _log.WriteLog("gRPC", $"Error sending heartbeat: {ex}");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // every 10 seconds
            }
        }



        //private async Task StartRabbitMQEvents(string branchId, CancellationToken stoppingToken)
        //{
        //    try
        //    {
        //        {
        //            await _log.WriteLog("RabbitMQ Init ", $"RabbitHelperInit Oky");

        //            await _rabbit.SubscribeBranchQueue(branchId, async (msg) =>
        //            {
        //                await _log.WriteLog("Get BancrQue First ", $"SubscribeBranchQueue Comming");

        //                //_logger.LogInformation($"Message received: {msg}");
        //                var job = JsonSerializer.Deserialize<BranchJobRequest<FileDetails>>(msg);
        //                try
        //                {
        //                    if (job == null)
        //                    {
        //                        await _log.WriteLog("Service Error ", $"Service Error Check ExecptionLog -> (Service Exception -> Job is null) ");
        //                        return;
        //                    }
        //                    await _log.WriteLog("Get BancrQue First ", $"SubscribeBranchQueue Comming 02 Oky");

        //                    if (job.jobType == "SFTP")
        //                    {
        //                        try
        //                        {
        //                            if (job.jobcommand == "DownloadGlobalUpdate")
        //                            {
        //                                await _log.WriteLog("DownloadGlobalUpdate ", $"DownloadGlobalUpdate Try");
        //                                if (job.jobRqValue != null)
        //                                {
        //                                    if (job.jobRqValue.server != null && job.jobRqValue.branch != null)
        //                                    {
        //                                        await _sftp.DownloadFileAsync($"{job.jobRqValue.server.path}/{job.jobRqValue.server.name}", $"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}");
        //                                        await _log.WriteLog("DownloadGlobalUpdate ", $"DownloadGlobalUpdate Done");

        //                                        await _rabbit.SendStatusOnlyPatch(branchId, "SFTP", "jh", true, 1, null, "DownloadOneFile");

        //                                        await _log.WriteLog("DownloadGlobalUpdate ", $"Send RabbitMq Status Done DownloadGlobalUpdate");
        //                                    }
        //                                }
        //                            }
        //                            else if (job.jobcommand == "DownloadGlobalUpdateAllBranch")
        //                            {
        //                                if (job.jobRqValue != null)
        //                                {
        //                                    if (job.jobRqValue.server != null && job.jobRqValue.branch != null)
        //                                    {
        //                                        await _log.WriteLog("DownloadGlobalUpdate ", $"DownloadGlobalUpdate Try");

        //                                        await _sftp.DownloadFileAsync($"{job.jobRqValue.server.path}/{job.jobRqValue.server.name}", $"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}");

        //                                        await _log.WriteLog("DownloadGlobalUpdate ", $"DownloadGlobalUpdate Done");

        //                                        await _rabbit.SendStatusAllOnlyPatch(branchId, "SFTP", "jh", true, 1, null, "DownloadGlobalFile");

        //                                        await _log.WriteLog("DownloadGlobalUpdate ", $"Send RabbitMq Status Done DownloadGlobalUpdate");
        //                                    }
        //                                }
        //                            }
        //                            else if (job.jobcommand == "UploadFileToMain")
        //                            {
        //                                if (job.jobRqValue != null)
        //                                {
        //                                    if (job.jobRqValue.server != null && job.jobRqValue.branch != null)
        //                                    {
        //                                        await _log.WriteLog("UploadFileToMain ", $"UploadFileToMain Try");

        //                                        //string remoteFile = "/upload/noVNC-1.6.0.zip"; // NOT C:/...
        //                                        await _sftp.UploadFileAsync($"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}", $"{job.jobRqValue.server.path}/{job.jobRqValue.server.name}");

        //                                        await _log.WriteLog("UploadFileToMain ", $"UploadFileToMain Done");

        //                                        await _rabbit.SendStatusOnlyPatch(branchId, "SFTP", "jh", true, 1, null, "UploadOneFile");

        //                                        await _log.WriteLog("UploadFileToMain ", $"all oky");
        //                                    }
        //                                }
        //                            }

        //                            else if (job.jobcommand == "SendToFolderStructure")
        //                            {
        //                                if (job.jobRqValue != null)
        //                                {
        //                                    if (job.jobRqValue.branch != null)
        //                                    {
        //                                        var folder = new GetFolderStructure();
        //                                        var rootNode = await folder.GetFolderStructureRootAsync($"{job.jobRqValue.branch.path}/{job.jobRqValue.branch.name}");

        //                                        if (rootNode != null)
        //                                        {
        //                                            await _rabbit.SendStatusOnlySendFolderStucher(branchId, "SFTP", "Success", true, 2, rootNode, "UploadFolderStructure");

        //                                            await _log.WriteLog("UploadFileToMain", "Folder structure sent successfully.");
        //                                        }
        //                                        else
        //                                        {
        //                                            await _rabbit.SendStatusOnlySendFolderStucher(branchId, "SFTP", "Error", false, 1, rootNode, "UploadFolderStructure");

        //                                        }
        //                                        //await PrintFolderNode(rootNode);

        //                                        //var options = new JsonSerializerOptions
        //                                        //{
        //                                        //    WriteIndented = true
        //                                        //};
        //                                        //string jsonMessage = JsonSerializer.Serialize(rootNode, options);

        //                                        //await _log.WriteLog("UploadFileToMain ", $"all oky");*/

        //                                    }
        //                                }
        //                            }
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            await _log.WriteLog("Service Error ", $"Service Error Check ExecptionLog -> (Service Exception ->SFTP Event) ");
        //                            await _log.WriteLog("Service Exception (SubscribeBranchQueue->SFTP Event)", $"Unexpected error: {ex}", 3);
        //                            await _log.WriteLog("Service Error (DealyLog/ExecptionLog ->SFTP Event)", "Service Error ", 2);
        //                        }

        //                    }

        //                }
        //                catch (Exception ex)
        //                {
        //                    //await SendStatusBranch($"Error: {ex.Message}", job.file.name, _rabbit);
        //                    await _log.WriteLog("Service Error ", $"Service Error Check ExecptionLog -> (Service Exception) ");
        //                    await _log.WriteLog("Service Exception (SubscribeBranchQueue)", $"Unexpected error: {ex}", 3);
        //                    await _log.WriteLog("Service Error (DealyLog/ExecptionLog)", "Service Error ", 2);

        //                }
        //            });
        //            async Task PrintFolderNode(FolderNode node, string indent = "")
        //            {
        //                await _log.WriteLog("folder", $"{indent}[{node.Name}] - {node.FullPath}");

        //                foreach (var file in node.Files)
        //                {
        //                    // file.SizeBytes = raw size in bytes
        //                    // file.SizeFormatted = human-readable (KB, MB, GB)
        //                    await _log.WriteLog("folder", $"{indent}  {file.Name} ({file.SizeFormatted}) - {file.FullPath}");
        //                }

        //                foreach (var sub in node.SubFolders)
        //                    await PrintFolderNode(sub, indent + "  ");
        //            }

        //        }



        //    }
        //    catch (TaskCanceledException)
        //    {
        //        // Service is stopping, exit
        //    }
        //}

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

