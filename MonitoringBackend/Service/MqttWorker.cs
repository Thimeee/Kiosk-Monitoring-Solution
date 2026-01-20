using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Enum;
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
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, DateTime> _lastHealthUpdate;
        private const int MaxConcurrentHeavyOperations = 100;
        private readonly TimeSpan _healthThrottleInterval = TimeSpan.FromMilliseconds(100);

        public MqttWorker(
            MQTTHelper mqtt,
            IConfiguration config,
            LoggerService log,
            IHubContext<BranchHub> hubContext,
            IServiceScopeFactory scopeFactory)
        {
            _mqtt = mqtt;
            _config = config;
            _log = log;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _semaphore = new SemaphoreSlim(MaxConcurrentHeavyOperations);
            _lastHealthUpdate = new ConcurrentDictionary<string, DateTime>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _mqtt.OnReconnectedMQTT += async () =>
            {
                await _log.WriteLog("Worker", "Reconnected → Re-subscribing queues...");
                await StartMQTTEvent(stoppingToken);
            };

            await InitializeMqttConnection(stoppingToken);
        }

        private async Task InitializeMqttConnection(CancellationToken stoppingToken)
        {
            int attempt = 0;
            const int maxRetries = 10;
            const int retryDelayMs = 3000;

            while (!stoppingToken.IsCancellationRequested)
            {
                await _log.WriteLog("MQTT Init", "Attempting MQTT connection...");

                bool connected = await _mqtt.InitAsync("127.0.0.1", "MQTTUser", "Thimi@1234", 1883);

                if (connected)
                {
                    await _log.WriteLog("MQTT Init", "MQTT connection successful");
                    await StartMQTTEvent(stoppingToken);
                    break;
                }

                attempt++;
                await RetryConnectionAsync(attempt, maxRetries, retryDelayMs, stoppingToken);

                if (attempt >= maxRetries)
                {
                    await _log.WriteLog("MQTT Init", "Max retries reached. Exiting.", 3);
                    break;
                }
            }
        }


        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _log.WriteLog("MqttWorker", "Service stopping gracefully...");

                // Shutdown MQTT gracefully
                await _mqtt.ShutdownAsync();

                await _log.WriteLog("MqttWorker", "Service stopped cleanly");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MqttWorker Error", $"Stop error: {ex.Message}", 3);
            }

            await base.StopAsync(cancellationToken);
        }

        private async Task StartMQTTEvent(CancellationToken stoppingToken)
        {
            try
            {
                await _log.WriteLog("MQTT", "Subscribing to server/#...");

                await _mqtt.SubscribeAsync("server/#", async (payload, topic) =>
                {
                    // Fire-and-forget with proper exception handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleBranchMessage(topic, payload, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            await _log.WriteLog("MQTT Handler Fatal", $"Topic: {topic}, Error: {ex.Message}", 3);
                        }
                    }, stoppingToken);
                });

                await _log.WriteLog("MQTT", "Subscription completed for all branches");
            }
            catch (TaskCanceledException)
            {
                await _log.WriteLog("MQTT", "Service stopping gracefully");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Exception", $"Service failed: {ex.Message}", 3);
            }
        }

        private async Task HandleBranchMessage(string topic, string payload, CancellationToken stoppingToken)
        {
            var parts = topic.Split('/');
            if (parts.Length < 3) return;

            string branchId = parts[1];
            string source = parts[2];
            string subTopic = parts.Length > 3 ? string.Join('/', parts.Skip(3)) : "";

            try
            {
                switch (source)
                {
                    case "SFTP":
                        await HandleSFTP(branchId, subTopic, payload);
                        break;

                    case "HEALTH":
                        await HandleHealth(branchId, payload, stoppingToken);
                        break;
                    case "PATCH":
                        await HandlePatch(branchId, payload, stoppingToken);
                        break;

                    default:
                        if (branchId == "mainServer" && source == "PATCHPROCESS")
                        {
                            await HandleServerPatchProcess(payload);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Handler Exception", $"Topic: {topic}, Error: {ex.Message}", 3);
            }
        }

        private async Task HandleHealth(string branchId, string payload, CancellationToken stoppingToken)
        {
            // Throttle health updates per branch
            if (_lastHealthUpdate.TryGetValue(branchId, out var lastTime))
            {
                if (DateTime.Now - lastTime < _healthThrottleInterval)
                {
                    return; // Skip this update
                }
            }

            _lastHealthUpdate[branchId] = DateTime.Now;

            // SignalR send is fast - NO SEMAPHORE NEEDED
            try
            {
                await _hubContext.Clients
                    .Group(BranchHub.BranchGroup(branchId))
                    .SendAsync("PerformanceUpdate", payload, stoppingToken);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("SignalR Send Error", $"Branch: {branchId}, Error: {ex.Message}", 3);
            }
        }

        private async Task HandlePatch(string branchId, string payload, CancellationToken stoppingToken)
        {

            var jobRes = JsonSerializer.Deserialize<PatchStatusUpdateMqttResponse>(payload);
            if (jobRes != null)
            {
                // Use semaphore for DB operations (heavy)
                await _semaphore.WaitAsync();
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();


                    int numericPatchId = int.Parse(
                        new string(jobRes.PatchId.TakeWhile(char.IsDigit).ToArray())
                    );

                    var branch = await db.Branches.FirstOrDefaultAsync(j => j.BranchId == branchId);

                    var patch = await db.PatchAssignBranchs.FirstOrDefaultAsync(j => j.PId == numericPatchId && j.Id == branch.Id);
                    if (patch != null)
                    {
                        var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == patch.JobUId);

                        if (job != null)
                        {
                            if (jobRes.Step == PatchStep.COMPLETE || jobRes.Step == PatchStep.ERROR)
                            {
                                patch.Endtime = jobRes.Timestamp;
                                job.JobEndTime = jobRes.Timestamp;
                                job.JSId = 3;

                            }
                            patch.Status = jobRes.Status;
                            patch.ProcessLevel = jobRes.Step;
                            patch.Message = jobRes.Message;

                            job.JobActive = 2;
                            job.JSId = 2;

                            await db.SaveChangesAsync();
                        }

                    }
                }
                finally
                {
                    _semaphore.Release();
                }
                if (jobRes.PatchRequestType == PatchRequestType.SINGLE_BRANCH_PATCH)
                {
                    await _hubContext.Clients
                        .Group(BranchHub.BranchGroup(branchId))
                        .SendAsync("SingleBranchPatchResponse", jobRes, stoppingToken);
                }
            }
        }



        private async Task HandleSFTP(string branchId, string subTopic, string payload)
        {
            switch (subTopic)
            {
                case "FolderStucherResponse":
                    var jobRes = JsonSerializer.Deserialize<BranchJobResponse<FolderNode>>(payload);
                    if (jobRes != null)
                    {
                        await _hubContext.Clients
                            .Group(BranchHub.UserAndBranchGroup(jobRes.jobUser, branchId))
                            .SendAsync("BranchUpdate", payload);
                    }
                    break;

                case "UploadResponse":
                    await HandleUploadResponse(branchId, payload);
                    break;

                case "DownloadResponse":
                    await HandleDownloadResponse(branchId, payload);
                    break;

                case "DownloadProgress":
                    await _hubContext.Clients
                        .Group(BranchHub.BranchGroup(branchId))
                        .SendAsync("DownloadFileProgress", payload);
                    break;

                case "UploadProgress":
                    await _hubContext.Clients
                        .Group(BranchHub.BranchGroup(branchId))
                        .SendAsync("UploadFileProgress", payload);
                    break;

                case "DeleteResponse":
                    await HandleDeleteResponse(branchId, payload);
                    break;
            }
        }

        private async Task HandleDeleteResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);
            if (res?.jobRsValue == null) return;

            // Use semaphore for DB operations (heavy)
            await _semaphore.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);
                if (job != null)
                {
                    job.JSId = res.jobRsValue.jobStatus == 2 ? 3 : 4;
                    job.JobActive = 2;
                    job.JobEndTime = res.jobEndTime;
                    job.JobMassage = res.jobRsValue.jobStatus == 2
                        ? "File deleted successfully"
                        : "Failed to delete file";

                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                _semaphore.Release();
            }

            await _hubContext.Clients
                .Group(BranchHub.BranchGroup(branchId))
                .SendAsync("DeleteResponse", payload);
        }

        private async Task HandleDownloadResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);
            if (res?.jobRsValue == null) return;

            // Use semaphore for DB operations (heavy)
            await _semaphore.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);
                if (job != null)
                {
                    switch (res.jobRsValue.jobStatus)
                    {
                        case 2:
                            job.JSId = 3;
                            job.JobActive = 2;
                            job.JobEndTime = res.jobEndTime;
                            job.JobMassage = "File downloaded successfully";
                            break;
                        case 1:
                            job.JSId = 2;
                            job.JobMassage = "File download in progress";
                            break;
                        default:
                            job.JSId = 4;
                            job.JobActive = 2;
                            job.JobEndTime = res.jobEndTime;
                            job.JobMassage = "Failed to download file";
                            break;
                    }

                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                _semaphore.Release();
            }

            await _hubContext.Clients
                .Group(BranchHub.BranchGroup(branchId))
                .SendAsync("DownloadFile", payload);
        }

        private async Task HandleUploadResponse(string branchId, string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobResponse<JobDownloadResponse>>(payload);
            if (res?.jobRsValue == null) return;

            // Use semaphore for DB operations (heavy)
            await _semaphore.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);
                if (job != null)
                {
                    switch (res.jobRsValue.jobStatus)
                    {
                        case 2:
                            job.JSId = 3;
                            job.JobActive = 2;
                            job.JobEndTime = res.jobEndTime;
                            job.JobMassage = "File uploaded successfully";
                            break;
                        case 1:
                            job.JSId = 2;
                            job.JobMassage = "File upload in progress";
                            break;
                        default:
                            job.JSId = 4;
                            job.JobActive = 2;
                            job.JobEndTime = res.jobEndTime;
                            job.JobMassage = "Failed to upload file";
                            break;
                    }

                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                _semaphore.Release();
            }

            await _hubContext.Clients
                .Group(BranchHub.BranchGroup(branchId))
                .SendAsync("UploadFile", payload);
        }

        private async Task HandleServerPatchProcess(string payload)
        {
            var res = JsonSerializer.Deserialize<BranchJobRequest<ServerPatch>>(payload);
            if (res == null) return;

            int newPatchId = int.TryParse(res.jobUser, out var id) ? id : 0;

            await _semaphore.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var newPatch = await db.NewPatches.FirstOrDefaultAsync(p => p.PId == newPatchId);
                if (newPatch == null) return;

                Job? job = await db.Jobs.FirstOrDefaultAsync(j => j.JobUId == res.jobId);

                if (res.jobRqValue == null) return;

                // Validate patch metadata
                if (newPatch.PatchFileName != res.jobRqValue.ChunksFileName ||
                    newPatch.PatchFilePath != res.jobRqValue.ChunksPath ||
                    newPatch.PatchZipName != res.jobRqValue.ZipName)
                {
                    await FileUploadFailed(newPatch, job, newPatchId, db);
                    await _hubContext.Clients.All.SendAsync("PatchDeploymentFailed", newPatch.PId);
                    return;
                }

                // Ensure chunk folder exists
                string chunksFolder = res.jobRqValue.ChunksPath;
                if (!Directory.Exists(chunksFolder))
                {
                    await FileUploadFailed(newPatch, job, newPatchId, db);
                    await _hubContext.Clients.All.SendAsync("PatchDeploymentFailed", newPatch.PId);
                    return;
                }

                // Count total chunks in folder
                int totalChunks = Directory.EnumerateFiles(chunksFolder).Count();

                // Check if all chunks have arrived
                if (newPatch.ServerSendChunks != totalChunks)
                {
                    await FileUploadFailed(newPatch, job, newPatchId, db);
                    await _hubContext.Clients.All.SendAsync("PatchDeploymentFailed", newPatch.PId);
                    return;

                }

                // Get Server Patch Folder
                var patchesFolder = await db.ServerFolderPaths
                    .Where(p => p.Name == "SR_P")
                    .Select(p => p.ServerFolderPathValue)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(patchesFolder))
                {
                    await _log.WriteLog("PatchProcess Error", "Server patch folder not configured", 3);
                    await _hubContext.Clients.All.SendAsync("PatchDeploymentFailed", newPatch.PId);
                    return;
                }

                // Update status → merging started
                if (job != null)
                {
                    newPatch.PatchProcessLevel = 2;
                    job.JSId = 2;
                    await db.SaveChangesAsync();
                }

                // Decide merged file path
                string mergedFilePath = Path.Combine(patchesFolder, $"{res.jobRqValue.ZipName}.zip");

                // Merge chunks
                await MergeChunks(chunksFolder, mergedFilePath);

                // Cleanup chunk folder
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                // Final update → completed
                if (job != null)
                {
                    newPatch.PatchZipPath = patchesFolder;
                    newPatch.PatchProcessLevel = 3;
                    newPatch.PatchActiveStatus = 2;

                    job.JSId = 3;
                    job.JobActive = 2;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = "File uploaded and zipped successfully";

                    await db.SaveChangesAsync();
                    // Notify complete
                    await _hubContext.Clients.All.SendAsync("PatchDeploymentComplete", newPatch.PId);
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("PatchProcess Error", ex.ToString(), 3);

            }
            finally
            {
                _semaphore.Release();
            }
        }


        private async Task FileUploadFailed(NewPatch patch, Job? job, int newPatchId, AppDbContext db)
        {
            try
            {
                string chunksFolder = patch?.PatchFilePath ?? "";
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                if (job != null)
                {
                    job.JSId = 4;
                    job.JobActive = 2;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"File upload failed";

                    db.Jobs.Update(job);
                }

                if (patch != null)
                {
                    patch.PatchActiveStatus = 1;
                    patch.PatchProcessLevel = 4;
                    db.NewPatches.Update(patch);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await _log.WriteLog("FileUploadFailed Error", ex.ToString(), 3);
            }
        }


        private async Task MergeChunks(string chunksPath, string outputFile)
        {
            if (!Directory.Exists(chunksPath))
                throw new DirectoryNotFoundException($"Chunks folder not found: {chunksPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

            var chunkFiles = Directory.EnumerateFiles(chunksPath)
                .Select(f => new
                {
                    File = f,
                    Index = int.TryParse(Path.GetFileNameWithoutExtension(f), out var i) ? i : -1
                })
                .Where(x => x.Index >= 0)
                .OrderBy(x => x.Index)
                .Select(x => x.File)
                .ToList();

            if (!chunkFiles.Any())
                throw new InvalidOperationException("No valid chunk files found.");

            await using var outFs = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            foreach (var chunkFile in chunkFiles)
            {
                await using var inFs = new FileStream(chunkFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await inFs.CopyToAsync(outFs);
            }
        }



        //private async Task MergeScripts(AppDbContext db, int? patchTypeId, string scriptsFolder)
        //{
        //    var scripts = await db.PatchScripts
        //        .Where(s => s.PTId == patchTypeId && s.ScriptActiveStatus == 1)
        //        .OrderBy(s => s.SId)
        //        .ToListAsync();

        //    foreach (var script in scripts)
        //    {
        //        if (string.IsNullOrWhiteSpace(script.ScriptContenct))
        //            continue;

        //        var scriptPath = Path.Combine(scriptsFolder, script.ScriptName);
        //        await File.WriteAllTextAsync(scriptPath, script.ScriptContenct, Encoding.UTF8);
        //    }
        //}

        //private async Task<string> CreatePatchZip(string patchesFolder, string sourceFolder, string zipName)
        //{
        //    string zipFile = Path.Combine(patchesFolder, $"{zipName}.zip");

        //    if (File.Exists(zipFile))
        //        File.Delete(zipFile);

        //    using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);

        //    foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
        //    {
        //        var entryName = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
        //        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

        //        using var entryStream = entry.Open();
        //        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
        //        await fs.CopyToAsync(entryStream);
        //    }

        //    return zipFile;
        //}

        //private async Task CleanupFailedPatch(
        //    BranchJobRequest<ServerPatch> request,
        //    NewPatch patch,
        //    Job job,
        //    string mergedFolder,
        //    string zipFile,
        //    AppDbContext db)
        //{
        //    try
        //    {
        //        if (Directory.Exists(request.jobRqValue.ChunksPath))
        //            Directory.Delete(request.jobRqValue.ChunksPath, true);

        //        if (mergedFolder != null && Directory.Exists(mergedFolder))
        //            Directory.Delete(mergedFolder, true);

        //        if (zipFile != null && File.Exists(zipFile))
        //            File.Delete(zipFile);

        //        if (job != null)
        //        {
        //            patch.PatchProcessLevel = 4;
        //            patch.PatchActiveStatus = 2;

        //            job.JSId = 4;
        //            job.JobActive = 2;
        //            job.JobEndTime = DateTime.Now;
        //            job.JobMassage = "File upload failed";

        //            await db.SaveChangesAsync();
        //            await _hubContext.Clients.All.SendAsync("PatchDeploymentFailed", patch.PId);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        await _log.WriteLog("Cleanup Error", ex.Message, 3);
        //    }
        //}

        private async Task RetryConnectionAsync(int attempt, int maxRetries, int retryDelayMs, CancellationToken stoppingToken)
        {
            await _log.WriteLog("Service Warning", $"Initialization failed. Attempt {attempt}/{maxRetries}", 2);

            try
            {
                await Task.Delay(retryDelayMs, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Service stopping
            }
        }
    }
}