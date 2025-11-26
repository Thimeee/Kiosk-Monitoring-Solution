
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using MonitoringBackend.Helper;
using MonitoringBackend.SRHub;
using MQTTnet.Protocol;

namespace MonitoringBackend.Service
{


    public class MqttWorker : BackgroundService
    {
        private readonly MQTTHelper _mqtt;
        private readonly IConfiguration _config;
        private readonly LoggerService _log;
        private readonly IHubContext<BranchHub> _hubContext;

        public MqttWorker(MQTTHelper mqtt, IConfiguration config, LoggerService log, IHubContext<BranchHub> hubContext)
        {
            _mqtt = mqtt;
            _config = config;
            _log = log;
            _hubContext = hubContext;
        }
        protected override async Task<Task> ExecuteAsync(CancellationToken stoppingToken)
        {
            bool MQTTInit = false;

            _mqtt.OnReconnectedMQTT += async () =>
            {
                await _log.WriteLog("Worker", "Reconnected → Re-subscribing queues...");
                await StartMQTTEvent(stoppingToken);
            };

            int attempt = 0;
            const int maxRetries = 10;
            const int retryDelayMs = 3000;



            while (!MQTTInit && !stoppingToken.IsCancellationRequested)
            {
                await _log.WriteLog("MQTT Init ", $"MQTTHelperInit try");

                MQTTInit = await _mqtt.InitAsync("127.0.0.1", "MQTTUser", "Thimi@1234", 1883);
                await _log.WriteLog("MQTT Init ", $"MQTTHelperInit Oky");

                if (!MQTTInit)
                {
                    attempt++;
                    await RetryConnectionAsync(attempt, maxRetries, retryDelayMs, stoppingToken);

                }
                else
                {
                    await StartMQTTEvent(stoppingToken);

                }

            }


            return Task.CompletedTask;



        }



        private async Task StartMQTTEvent(CancellationToken stoppingToken)
        {
            try
            {
                await _log.WriteLog("MQTT", "MQTT Service initializing...");

                string topicFilter = "server/#"; // + = branchId, # = all subtopics
                await _mqtt.SubscribeAsync(topicFilter, async (payload, topic) =>
                {
                    // Offload processing to a background task for concurrency
                    _ = Task.Run(async () =>
                    {
                        await HandleBranchMessage(topic, payload);

                    });
                });

                await _log.WriteLog("MQTT", "Subscription completed for all branches.");

            }
            catch (TaskCanceledException)
            {
                await _log.WriteLog("MQTT", "MQTT service stopping gracefully.");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Exception", $"Service failed: {ex}");
            }
        }

        private async Task HandleBranchMessage(string topic, string payload)
        {
            try
            {
                var parts = topic.Split('/');
                if (parts.Length < 3) return; // Invalid topic

                string branchId = parts[1];
                string source = parts[2]; // e.g., SFTP, Hardware
                string subTopic = string.Join('/', parts.Skip(3));

                // Route by source type
                switch (source)
                {
                    case "SFTP":
                        await HandleSFTP(branchId, subTopic, payload);
                        break;
                    case "HEALTH":
                        if (subTopic == "PerformanceRespo")
                        {
                            await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                                .SendAsync("PerformanceUpdate", payload);
                        }

                        //await HandleHardware(branchId, subTopic, payload);
                        break;
                    default:
                        await _log.WriteLog("MQTT Unknown Source", $"Branch: {branchId}, Source: {source}, SubTopic: {subTopic}, Payload: {payload}");
                        break;
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Message Handler Exception", $"Topic: {topic}, Payload: {payload}, Error: {ex}");
            }
        }

        private async Task HandleSFTP(string branchId, string subTopic, string payload)
        {
            switch (subTopic)
            {
                case "FolderStucherResponse":
                    var jobRes = JsonSerializer.Deserialize<BranchJobResponse<FolderNode>>(payload);

                    //await _hubContext.Clients.All.SendAsync("BranchUpdate", payload);
                    await _hubContext.Clients.Group(BranchHub.UserAndBranchGroup(jobRes.jobUser, branchId))
                        .SendAsync("BranchUpdate", payload);
                    break;
                case "uploadResponse":
                    await _log.WriteLog("SFTP Upload", $"Branch: {branchId}, Payload: {payload}");
                    break;
                case "DownloadResponse":
                    var jobResDownlod = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

                    await _hubContext.Clients.Group(BranchHub.UserAndBranchGroup(jobResDownlod.jobUser, branchId))
                      .SendAsync("DownloadFile", payload);
                    break;

                case "DownloadProgress":
                default:
                    var jobResProgress = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

                    await _hubContext.Clients.Group(BranchHub.UserAndBranchGroup(jobResProgress.jobUser, branchId))
                      .SendAsync("DownloadFileProgress", payload);
                    break;
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


        public class BranchCommand
        {
            public string BranchId { get; set; }
            public string Action { get; set; }
            public string Payload { get; set; }
        }

    }
}
