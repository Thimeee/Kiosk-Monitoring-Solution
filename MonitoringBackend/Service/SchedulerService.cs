using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.Helper;
using MQTTnet.Protocol;

namespace MonitoringBackend.Service
{
    public class SchedulerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _semaphore;
        private readonly MQTTHelper _mqtt;
        private readonly LoggerService _log;

        private const int MAX_PARALLEL_JOBS = 10;
        private const int LOOP_DELAY_MS = 2000;

        public SchedulerService(
            IServiceScopeFactory scopeFactory,
            MQTTHelper mqtt,
            LoggerService log)
        {
            _scopeFactory = scopeFactory;
            _mqtt = mqtt;
            _log = log;
            _semaphore = new SemaphoreSlim(MAX_PARALLEL_JOBS);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _log.WriteLog("Scheduler", "Scheduler Service Started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var now = DateTime.Now;

                    // Get Due Jobs
                    var dueJobs = await db.PatchAssignBranchs
                        .Where(p =>
                            p.StartTime <= now &&
                            p.Status == PatchStatus.SCHEDULE)
                        .AsNoTracking()
                        .ToListAsync(stoppingToken);

                    foreach (var job in dueJobs)
                    {
                        await _semaphore.WaitAsync(stoppingToken);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (job.StartTime < now.AddMinutes(-30))
                                {
                                    await PatchEror(job.Id, stoppingToken);
                                }
                                else
                                {
                                    await MarkJobAsProcessing(job.Id, stoppingToken);
                                    await ProcessScheduledPatch(job.Id, stoppingToken);
                                }

                            }
                            catch (Exception ex)
                            {
                                await _log.WriteLog("Scheduler Job Error", ex.ToString());
                            }
                            finally
                            {
                                _semaphore.Release();
                            }

                        }, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("Scheduler Loop Error", ex.ToString());
                }

                await Task.Delay(LOOP_DELAY_MS, stoppingToken);
            }

            await _log.WriteLog("Scheduler", "Scheduler Service Stopped");
        }

        // ----------------------------------------------------

        private async Task MarkJobAsProcessing(int? patchAssignId, CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.PatchAssignBranchs
                .FirstOrDefaultAsync(x => x.Id == patchAssignId, token);

            if (job != null && job.Status == PatchStatus.SCHEDULE)
            {
                job.Status = PatchStatus.INIT;
                await db.SaveChangesAsync(token);
            }
        }


        private async Task PatchEror(int? patchAssignId, CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.PatchAssignBranchs
                .FirstOrDefaultAsync(x => x.Id == patchAssignId, token);

            if (job != null && job.Status == PatchStatus.SCHEDULE)
            {
                job.Status = PatchStatus.FAILED;
                job.ProcessLevel = PatchStep.ERROR;
                job.Message = "Application update failed. Branch is offline or network connection failed.";

                await db.SaveChangesAsync(token);
            }
        }
        // ----------------------------------------------------

        private async Task ProcessScheduledPatch(int? patchAssignId, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var patchAss = await db.PatchAssignBranchs
                    .FirstOrDefaultAsync(x => x.Id == patchAssignId, stoppingToken);

                if (patchAss == null) return;

                var patch = await db.NewPatches
                    .FirstOrDefaultAsync(p =>
                        p.PatchProcessLevel == 3 &&
                        p.PId == patchAss.PId, stoppingToken);

                if (patch == null)
                {
                    await _log.WriteLog("Scheduler", $"Patch not found {patchAss.PId}");
                    return;
                }

                var branch = await db.Branches
                    .FirstOrDefaultAsync(b =>
                        b.Id == patchAss.Id &&
                        b.TerminalActiveStatus == TerminalActive.TERMINAL_ONLINE,
                        stoppingToken);

                if (branch == null)
                {
                    await _log.WriteLog("Scheduler",
                        $"Branch offline. Skip Patch {patch.PId}");
                    return;
                }

                var zipPath = Path.Combine(
                    patch.PatchZipPath,
                    $"{patch.PatchZipName}.zip");

                if (!File.Exists(zipPath))
                {
                    await _log.WriteLog("Scheduler",
                        $"Patch File Missing : {zipPath}");
                    return;
                }

                var checksum = await GetChecksumAsync(zipPath);

                var payload = new PatchDeploymentMqttRequest
                {
                    UserId = "Scheduler",
                    PatchId = patch.PId.ToString(),
                    PatchZipPath = zipPath,
                    ExpectedChecksum = checksum,
                    Status = PatchStatus.INIT,
                    PatchRequestType = PatchRequestType.ALL_BRANCH_PATCH,
                    Step = PatchStep.START
                };

                var topic = $"branch/{branch.TerminalId}/PATCH/Application";

                if (patch.PTId == 1)
                {
                    await _mqtt.PublishToServer(
                        payload,
                        topic,
                        MqttQualityOfServiceLevel.ExactlyOnce);
                }
                else
                {
                    await _log.WriteLog("Scheduler",
                        $"Patch Type {patch.PTId} Not Implemented");
                }

                patchAss.ProcessLevel = PatchStep.START;

                db.Update(patchAss);
                await db.SaveChangesAsync(stoppingToken);

                await _log.WriteLog("Scheduler",
                    $"Patch {patch.PId} Sent → {branch.BranchName}");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Scheduler Process Error", ex.ToString());
            }
        }

        // ----------------------------------------------------

        private async Task<string> GetChecksumAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);

            var hash = await sha256.ComputeHashAsync(stream);

            return BitConverter
                .ToString(hash)
                .Replace("-", "")
                .ToLowerInvariant();
        }
    }
}
