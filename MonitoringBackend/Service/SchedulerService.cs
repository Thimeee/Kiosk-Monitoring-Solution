using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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
        private readonly SemaphoreSlim _semaphore = new(10); // Max 10 scheduled jobs at a time
        private readonly MQTTHelper _mqtt;
        private readonly LoggerService _log;

        public SchedulerService(IServiceScopeFactory scopeFactory, MQTTHelper mqtt, LoggerService log)
        {
            _scopeFactory = scopeFactory;
            _mqtt = mqtt;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Get scheduled patches which are due
                    var dueJobs = await db.PatchAssignBranchs
                                          .Where(p => p.StartTime <= DateTime.Now
                                                   && p.Status == PatchStatus.SCHEDULE)
                                          .ToListAsync(stoppingToken);

                    if (dueJobs.Count > 0)
                    {
                        foreach (var job in dueJobs)
                        {
                            await _semaphore.WaitAsync(stoppingToken);
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // Enqueue or directly call patch handler
                                    // Example: call MQTT worker method or queue
                                    await ProcessScheduledPatch(job, stoppingToken);
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, stoppingToken);
                        }
                    }


                }
                catch (Exception ex)
                {
                    // log error
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task ProcessScheduledPatch(PatchAssignBranch patchass, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var patch = await db.NewPatches
                    .Where(p => p.PatchProcessLevel == 3 && p.PId == patchass.PId)
                    .FirstOrDefaultAsync();

                if (patch == null) return;

                var branch = await db.Branches
                    .Where(b => b.Id == patchass.Id && b.TerminalActiveStatus == TerminalActive.TERMINAL_ONLINE)
                    .FirstOrDefaultAsync();

                if (branch == null)
                {
                    await _log.WriteLog("Patch Scheduler", $"Branch  offline, skipping patch {patch.PId}");
                    return;
                }

                var zipPath = Path.Combine(patch.PatchZipPath, $"{patch.PatchZipName}.zip");
                if (!File.Exists(zipPath))
                {
                    await _log.WriteLog("Patch Scheduler", $"Patch file missing: {zipPath}");
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
                    await _mqtt.PublishToServer(payload, topic, MqttQualityOfServiceLevel.ExactlyOnce);
                else
                    await _log.WriteLog("Patch Scheduler", $"Patch type {patch.PTId} not implemented");

                patchass.Status = PatchStatus.INIT;
                patchass.ProcessLevel = PatchStep.START;
                await db.SaveChangesAsync(stoppingToken);

                await _log.WriteLog("Patch Scheduler", $"Scheduled patch {patch.PId} sent to branch {branch.BranchName}");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Scheduler Error", ex.ToString());
            }
        }

        private async Task<string> GetChecksumAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);

            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
