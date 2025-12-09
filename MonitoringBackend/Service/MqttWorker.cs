
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
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
        private readonly IServiceScopeFactory _scopeFactory;


        public MqttWorker(MQTTHelper mqtt, IConfiguration config, LoggerService log, IHubContext<BranchHub> hubContext, IServiceScopeFactory scopeFactory)
        {
            _mqtt = mqtt;
            _config = config;
            _log = log;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;

        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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


            //return Task.CompletedTask;



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
                case "UploadResponse":
                    //var jobResUpload = JsonSerializer.Deserialize<BranchJobResponse<FolderNode>>(payload);

                    await HandleUploadResponse(branchId, payload);

                    break;
                case "DownloadResponse":
                    //var jobResDownload = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);


                    await HandleDownloadResponse(branchId, payload);
                    break;

                case "DownloadProgress":

                    //var jobResProgressDownload = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

                    await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                      .SendAsync("DownloadFileProgress", payload);
                    break;

                case "UploadProgress":

                    //var jobResProgressUpload = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

                    await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                      .SendAsync("UploadFileProgress", payload);
                    break;
                case "DeleteResponse":


                    await HandleDeleteResponse(branchId, payload);
                    break;

                default:
                    //await _log.WriteLog("MQTT Unknown SFTP SubTopic", $"Branch: {branchId}, SubTopic: {subTopic}, Payload: {payload}");
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

        //Branch File Delete method Start 
        private async Task HandleDeleteResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);


            if (res == null || res.jobRsValue == null)
                return;

            // → Create new DbContext safely
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);

            if (job != null)
            {
                if (res.jobRsValue.jobStatus == 2)
                {
                    job.JobStatus = 2;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Deleted successfully";
                }
                else
                {
                    job.JobStatus = 0;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "Failed to delete file";
                }

                db.Jobs.Update(job);
                await db.SaveChangesAsync();
            }

            await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                .SendAsync("DeleteResponse", payload);
        }

        //Branch File Delete method Start 


        //Branch File Download method Start 
        private async Task HandleDownloadResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

            if (res == null || res.jobRsValue == null)
                return;

            // → Create new DbContext safely
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);

            if (job != null)
            {
                if (res.jobRsValue.jobStatus == 2)
                {
                    job.JobStatus = 2;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Download successfully";
                }
                else
                {
                    job.JobStatus = 0;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "Failed to Download file";
                }

                db.Jobs.Update(job);
                await db.SaveChangesAsync();
            }

            await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                       .SendAsync("DownloadFile", payload);

        }

        //Branch File Download method End 

        //Branch File Upload method Start 
        private async Task HandleUploadResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);

            if (res == null || res.jobRsValue == null)
                return;

            // → Create new DbContext safely
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);

            if (job != null)
            {
                if (res.jobRsValue.jobStatus == 2)
                {
                    job.JobStatus = 2;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Upload successfully";
                }
                else
                {
                    job.JobStatus = 0;
                    job.JobActive = 0;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "Failed to Upload file";
                }

                db.Jobs.Update(job);
                await db.SaveChangesAsync();
            }

            await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                   .SendAsync("UploadFile", payload);

        }

        //Branch File Upload method End 





        public class BranchCommand
        {
            public string BranchId { get; set; }
            public string Action { get; set; }
            public string Payload { get; set; }
        }

    }
}

