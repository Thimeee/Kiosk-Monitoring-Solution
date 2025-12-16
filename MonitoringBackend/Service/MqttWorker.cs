
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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


                //Branch Events process Start 

                // Route by source type
                switch (source)
                {

                    case "SFTP":
                        await HandleSFTP(branchId, subTopic, payload);
                        break;
                    case "HEALTH":
                        //if (subTopic == "PerformanceRespo")
                        //{
                        await _hubContext.Clients.Group(BranchHub.BranchGroup(branchId))
                            .SendAsync("PerformanceUpdate", payload);
                        //}

                        //await HandleHardware(branchId, subTopic, payload);
                        break;
                    default:
                        await _log.WriteLog("MQTT Unknown Source Branche", $"Branch: {branchId}, Source: {source}, SubTopic: {subTopic}, Payload: {payload}");
                        break;
                }
                //Branch Events process End 


                //Main Server backGround process Start
                if (branchId == "mainServer")
                {
                    switch (source)
                    {
                        case "PATCHPROCESS":
                            await ServerPatchProcess(payload);

                            break;

                        default:
                            await _log.WriteLog("MQTT Unknown Source Main Server", $"Branch: {branchId}, Source: {source}, SubTopic: {subTopic}, Payload: {payload}");
                            break;
                    }
                }
                //Main Server backGround process End 

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
                    job.JSId = 3;
                    job.JobActive = 2;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Deleted successfully";
                }
                else
                {
                    job.JSId = 4;
                    job.JobActive = 2;
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
                    job.JSId = 3;
                    job.JobActive = 2;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Download successfully";
                }
                else if (res.jobRsValue.jobStatus == 1)
                {
                    job.JSId = 2;
                    job.JobMassage = "File Download Start Now";
                }
                else
                {
                    job.JSId = 4;
                    job.JobActive = 2;
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
                    job.JSId = 3;
                    job.JobActive = 2;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = "File Upload successfully";
                }
                else if (res.jobRsValue.jobStatus == 1)
                {
                    job.JSId = 2;
                    job.JobMassage = "File Upload Start Now";
                }
                else
                {
                    job.JSId = 4;
                    job.JobActive = 2;
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


        //Server Patch Process  

        private async Task ServerPatchProcess(string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobRequest<ServerPatch>>(payload);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            NewPatch? newPatches;
            string? mergedFileFolder = null;
            string? zipFile = null;

            if (res == null)
                return;

            int newPatchId = int.TryParse(res.jobUser, out var id) ? id : 0;


            newPatches = await db.NewPatches.FirstOrDefaultAsync(j => j.PId == newPatchId);



            if (newPatches == null) return;

            try
            {
                var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);





                string PatchesFolder = Path.Combine("C:\\Branches\\MCS\\Patches\\AllNewPatches");
                if (!Directory.Exists(PatchesFolder))
                    Directory.CreateDirectory(PatchesFolder);

                mergedFileFolder = Path.Combine(PatchesFolder, res.jobRqValue.ZipName);

                if (!Directory.Exists(mergedFileFolder))
                    Directory.CreateDirectory(mergedFileFolder);

                var ApplicationFolder = Path.Combine(mergedFileFolder, "Application");
                var ScriptsFolder = Path.Combine(mergedFileFolder, "Scripts");
                var ReleaseFolder = Path.Combine(mergedFileFolder, "Release");

                if (!Directory.Exists(ApplicationFolder))
                    Directory.CreateDirectory(ApplicationFolder);
                if (!Directory.Exists(ScriptsFolder))
                    Directory.CreateDirectory(ScriptsFolder);
                if (!Directory.Exists(ReleaseFolder))
                    Directory.CreateDirectory(ReleaseFolder);

                var mergedFile = Path.Combine(ApplicationFolder, res.jobRqValue.ChunksFileName);

                //newPath table update 

                //job table update 
                if (job != null && newPatches != null)
                {
                    newPatches.PatchProcessLevel = 2;
                    job.JSId = 2;

                    db.Jobs.Update(job);
                    db.NewPatches.Update(newPatches);
                    await db.SaveChangesAsync();

                }


                //await _hubContext.Clients.Group(BranchHub.BranchGroup("mainServer"))
                //   .SendAsync("PatchCompleted", res.jobId);



                using (var outFs = new FileStream(mergedFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var chunkFile in Directory.GetFiles(res.jobRqValue.ChunksPath).OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f))))
                    {
                        using (var inFs = new FileStream(chunkFile, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buffer = new byte[1024 * 1024]; // 1 MB buffer
                            int read;
                            while ((read = await inFs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await outFs.WriteAsync(buffer, 0, read);
                            }
                        }
                    }

                }

                //merged scripts to Folders 

                var scripts = await db.PatchScripts
    .Where(s => s.PTId == newPatches.PTId && s.ScriptActiveStatus == 1)
    .OrderBy(s => s.SId)
    .ToListAsync();

                if (scripts != null)
                {

                    foreach (var script in scripts)
                    {
                        if (string.IsNullOrWhiteSpace(script.ScriptContenct))
                            continue;

                        // Safe file name

                        var scriptPath = Path.Combine(ScriptsFolder, script.ScriptName);

                        await System.IO.File.WriteAllTextAsync(
                            scriptPath,
                            script.ScriptContenct,
                            Encoding.UTF8
                        );
                    }
                }


                //Create To zip 

                zipFile = Path.Combine(PatchesFolder, res.jobRqValue.ZipName + ".zip");
                if (System.IO.File.Exists(zipFile))
                    System.IO.File.Delete(zipFile);

                using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);

                foreach (var file1 in Directory.GetFiles(mergedFileFolder, "*", SearchOption.AllDirectories))
                {
                    var entryName = Path.GetRelativePath(mergedFileFolder, file1).Replace('\\', '/');
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

                    using var entryStream = entry.Open();
                    using var fs = new FileStream(file1, FileMode.Open, FileAccess.Read);
                    byte[] buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await entryStream.WriteAsync(buffer, 0, read);
                    }
                }

                var dirToDelete = Directory.GetParent(res.jobRqValue.ChunksPath)?.Parent;

                if (dirToDelete != null && dirToDelete.Exists)
                {
                    Directory.Delete(dirToDelete.FullName, true);
                }


                if (Directory.Exists(mergedFileFolder))
                {
                    Directory.Delete(mergedFileFolder, true);
                }



                // Update Job

                if (job != null && newPatches != null)
                {
                    newPatches.PatchZipPath = zipFile;
                    newPatches.PatchProcessLevel = 3;
                    newPatches.PatchActiveStatus = 2;

                    job.JSId = 3;
                    job.JobActive = 2;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"File uploaded and zipped successfully";

                    db.Jobs.Update(job);
                    db.NewPatches.Update(newPatches);
                    await db.SaveChangesAsync();
                }

                // 1️⃣ Paths

                // 6️⃣ Notify

            }
            catch (Exception ex)
            { // Cleanup on failure
                try
                {
                    if (Directory.Exists(res.jobRqValue.ChunksPath))
                        Directory.Delete(res.jobRqValue.ChunksPath, true);

                    if (mergedFileFolder != null)
                    {
                        if (Directory.Exists(mergedFileFolder))
                        {
                            Directory.Delete(mergedFileFolder, true);
                        }
                    }

                    if (zipFile != null)
                    {
                        if (System.IO.File.Exists(zipFile))
                            System.IO.File.Delete(zipFile);
                    }



                    if (!string.IsNullOrEmpty(res.jobId) && newPatches != null)
                    {
                        var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);
                        if (job != null)
                        {
                            job.JSId = 4;
                            job.JobActive = 2;
                            job.JobEndTime = DateTime.Now;
                            job.JobMassage = $"File upload failed";

                            db.Jobs.Update(job);
                            await db.SaveChangesAsync();
                        }
                    }
                }
                catch { }
                //await _log.WriteLog("PatchProcess Error", ex.ToString(), 3);
            }






        }

        //Server Patch Process  





    }
}

