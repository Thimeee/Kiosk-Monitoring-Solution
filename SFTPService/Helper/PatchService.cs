using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Enum;
using MQTTnet.Protocol;
using SFTPService.Helper;
//using SFTPService.Models;

namespace SFTPService.Helper
{
    public interface IPatchService
    {
        Task<bool> ApplyPatchAsync(PatchDeploymentMqttRequest request, string branchId, CancellationToken cancellationToken);
    }


    public class PatchService : IPatchService
    {
        private readonly LoggerService _log;
        private readonly MQTTHelper _mqtt;
        private readonly SftpFileService _sftp;
        private readonly IConfiguration _config;

        // Configuration paths
        private readonly string _appName;
        private readonly string _secAppName;
        private readonly string _appFolder;
        private readonly string _backupRoot;
        private readonly string _updateRoot;
        private readonly string _downloadsPath;
        private readonly string _processName;
        private readonly string _SecprocessName;
        private readonly int _maxBackupsToKeep;

        public PatchService(
            LoggerService log,
            MQTTHelper mqtt,
            SftpFileService sftp,
            IConfiguration config)
        {
            _log = log;
            _mqtt = mqtt;
            _sftp = sftp;
            _config = config;

            // Load configuration
            _appName = config["Patch:MainAppName"] ?? "Bank_Cheque_printer.exe";
            _secAppName = config["Patch:SecondAppName"] ?? "Bank_Cheque_printer.exe";
            _appFolder = config["Patch:AppFolder"] ?? @"C:\Branches\Appliction\App";
            _backupRoot = config["Patch:BackupRoot"] ?? @"C:\Branches\Appliction\Backups";
            _updateRoot = config["Patch:UpdateRoot"] ?? @"C:\Branches\Appliction\Updates\NewVersion";
            _downloadsPath = config["Patch:DownloadsPath"] ?? @"C:\Branches\Appliction\Downloads";
            _maxBackupsToKeep = int.TryParse(config["Patch:MaxBackupsToKeep"], out var max) ? max : 5;

            _processName = Path.GetFileNameWithoutExtension(_appName);
            _SecprocessName = Path.GetFileNameWithoutExtension(_secAppName);
        }

        public async Task<bool> ApplyPatchAsync(
     PatchDeploymentMqttRequest request,
     string branchId,
     CancellationToken cancellationToken)
        {
            string backupPath = null;

            try
            {
                await _log.WriteLog("Patch", $"Starting patch deployment: JobId={request.PatchId}");

                // PHASE 1: DOWNLOAD (0-15%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.DOWNLOAD, "Downloading patch file", 5, cancellationToken);

                string downloadedZip = await DownloadPatchAsync(request, branchId, cancellationToken);
                if (downloadedZip == null)
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.DOWNLOAD, "Failed to download patch", 5, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                   PatchStatus.SUCCESS, PatchStep.DOWNLOAD, "Patch Downloading Successfully", 15, cancellationToken);

                // PHASE 2: VALIDATE (15-20%)
                //await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                //    PatchStatus.IN_PROGRESS, PatchStep.VALIDATE, "Validating checksum", 15, cancellationToken);

                if (!await ValidateChecksumAsync(downloadedZip, request.ExpectedChecksum))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.VALIDATE, "Checksum validation failed", 15, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }

                // PHASE 3: EXTRACT (20-30%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.EXTRACT, "Extracting patch files", 20, cancellationToken);

                if (!await ExtractPatchAsync(downloadedZip, cancellationToken))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.EXTRACT, "Failed to extract patch", 20, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.EXTRACT, "Patch extracted successfully", 30, cancellationToken);

                // PHASE 4: STOP APPLICATION (30-40%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.STOP_APP, "Stopping applications", 35, cancellationToken);

                if (!await StopApplicationAsync(_processName))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.STOP_APP, "Failed to stop main application", 35, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }

                if (!await StopApplicationAsync(_SecprocessName))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.STOP_APP, "Failed to stop secondary application", 35, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.STOP_APP, "Applications stopped successfully", 40, cancellationToken);

                // PHASE 5: BACKUP (40-55%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.BACKUP, "Creating backup", 45, cancellationToken);

                backupPath = await CreateBackupAsync(cancellationToken);
                if (string.IsNullOrEmpty(backupPath))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.FAILED, PatchStep.BACKUP, "Backup creation failed", 45, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    return false;
                }

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.BACKUP, $"Backup created: {Path.GetFileName(backupPath)}", 55, cancellationToken);

                // PHASE 6: APPLY UPDATE (55-75%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.UPDATE, "Applying update", 60, cancellationToken);

                if (!await ApplyUpdateAsync(cancellationToken))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.IN_PROGRESS, PatchStep.ROLLBACK, "Update failed - starting rollback", 65, cancellationToken);

                    await RollbackAsync(backupPath, cancellationToken);

                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.ROLLBACK, PatchStep.COMPLETE, "Rollback completed. System will restart.", 100, cancellationToken);

                    await Task.Delay(2000, cancellationToken);
                    await ScheduleSystemRestartAsync(cancellationToken);
                    return false;
                }

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.UPDATE, "Update applied successfully", 75, cancellationToken);

                // PHASE 7: START APP CHECK (75-80%) - Just verify files
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.START_APP, "Verifying updated files", 78, cancellationToken);

                if (!await StartApplicationAsync(cancellationToken))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.IN_PROGRESS, PatchStep.ROLLBACK, "File verification failed - rolling back", 80, cancellationToken);

                    await RollbackAsync(backupPath, cancellationToken);

                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.ROLLBACK, PatchStep.COMPLETE, "Rollback completed. System will restart.", 100, cancellationToken);

                    await Task.Delay(2000, cancellationToken);
                    await ScheduleSystemRestartAsync(cancellationToken);
                    return false;
                }

                // PHASE 8: VERIFY (80-90%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.VERIFY, "Verifying application integrity", 85, cancellationToken);

                if (!await VerifyApplicationAsync(cancellationToken))
                {
                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.IN_PROGRESS, PatchStep.ROLLBACK, "Verification failed - rolling back", 88, cancellationToken);

                    await RollbackAsync(backupPath, cancellationToken);

                    await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                        PatchStatus.ROLLBACK, PatchStep.COMPLETE, "Rollback completed. System will restart.", 100, cancellationToken);

                    await Task.Delay(2000, cancellationToken);
                    await ScheduleSystemRestartAsync(cancellationToken);
                    return false;
                }

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.VERIFY, "Application verified successfully", 90, cancellationToken);

                // PHASE 9: CLEANUP (90-95%)
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.CLEANUP, "Cleaning up temporary files", 92, cancellationToken);

                await CleanupAsync(downloadedZip);

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.IN_PROGRESS, PatchStep.CLEANUP, "Cleanup completed", 95, cancellationToken);

                // ✅ PHASE 10: COMPLETE (95-100%) - SEND SUCCESS **BEFORE** RESTARTING
                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.SUCCESS, PatchStep.COMPLETE, "Patch applied successfully. System will restart.", 100, cancellationToken);

                await _log.WriteLog("Patch", $"✓ Patch completed successfully: JobId={request.PatchId}");

                // ✅ Critical: Wait longer to ensure QoS 2 message is delivered
                await Task.Delay(3000, cancellationToken);

                // ✅ NOW schedule the restart (after everything is done)
                await ScheduleSystemRestartAsync(cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Error", $"JobId={request.PatchId}, Error: {ex}", 3);

                await PublishStatusAsync(request.UserId, request.PatchId, request.PatchRequestType, branchId,
                    PatchStatus.FAILED, PatchStep.ERROR, $"Unexpected error: {ex.Message}", 0, cancellationToken);

                await Task.Delay(2000, cancellationToken);

                // Attempt rollback if we have a backup
                if (!string.IsNullOrEmpty(backupPath))
                {
                    try
                    {
                        await RollbackAsync(backupPath, cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                        await ScheduleSystemRestartAsync(cancellationToken);
                    }
                    catch (Exception rollbackEx)
                    {
                        await _log.WriteLog("Rollback Error", $"{rollbackEx}", 3);
                    }
                }

                return false;
            }
        }

        private async Task<string> DownloadPatchAsync(
            PatchDeploymentMqttRequest request,
            string branchId,
            CancellationToken cancellationToken)
        {
            try
            {
                // Create downloads directory if not exists
                Directory.CreateDirectory(_downloadsPath);

                string localPath = Path.Combine(_downloadsPath, $"patch_{request.PatchId}.zip");

                await _log.WriteLog("Patch Download", $"Downloading from: {request.PatchZipPath}");

                await _sftp.DownloadFileAsync(
                    request.PatchZipPath,
                    localPath,
                    "system",
                    branchId,
                    request.PatchId,
                    null,
                    cancellationToken);

                await _log.WriteLog("Patch Download", $"Downloaded to: {localPath}");
                return localPath;
            }
            catch (Exception ex)
            {
                // Replace this line:
                await _log.WriteLog("Patch Download Error", $"{ex}", 3);

                return null;
            }
        }

        private async Task<bool> ValidateChecksumAsync(string filePath, string expectedChecksum)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expectedChecksum))
                {
                    await _log.WriteLog("Patch Validate", "No checksum provided - skipping validation", 2);
                    return true;
                }

                await _log.WriteLog("Patch Validate", "Computing SHA256 checksum");

                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await sha256.ComputeHashAsync(stream);
                var actualChecksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                var isValid = actualChecksum.Equals(expectedChecksum.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);

                if (isValid)
                {
                    await _log.WriteLog("Patch Validate", "Checksum valid");
                }
                else
                {
                    await _log.WriteLog("Patch Validate Error",
                        $"Checksum mismatch - Expected: {expectedChecksum}, Got: {actualChecksum}", 3);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Validate Error", $"{ex}", 3);
                return false;
            }
        }

        private async Task<bool> ExtractPatchAsync(string zipPath, CancellationToken cancellationToken)
        {
            try
            {
                // Clean update folder
                if (Directory.Exists(_updateRoot))
                {
                    await _log.WriteLog("Patch Extract", "Cleaning update folder");
                    Directory.Delete(_updateRoot, true);
                }

                Directory.CreateDirectory(_updateRoot);

                await _log.WriteLog("Patch Extract", $"Extracting to: {_updateRoot}");

                // Extract ZIP
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, _updateRoot), cancellationToken);

                // Verify extraction
                var fileCount = Directory.GetFiles(_updateRoot, "*", SearchOption.AllDirectories).Length;
                await _log.WriteLog("Patch Extract", $"Extracted {fileCount} files");

                return fileCount > 0;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Extract Error", $"{ex}", 3);
                return false;
            }
        }

        private async Task<bool> StopApplicationAsync(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length == 0)
                {
                    await _log.WriteLog("Patch Stop", $"Application {processName} not running");
                    return true;
                }

                await _log.WriteLog("Patch Stop", $"Stopping {processes.Length} instance(s) of {processName}");

                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLog("Patch Stop Warning", $"Failed to stop PID {process.Id}: {ex.Message}", 2);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                // Wait for clean shutdown
                await Task.Delay(2000);

                //  FIX: Verify stopped - check the CORRECT process name
                var remainingProcesses = Process.GetProcessesByName(processName);
                if (remainingProcesses.Length > 0)
                {
                    await _log.WriteLog("Patch Stop Error", $"{remainingProcesses.Length} process(es) of {processName} still running", 3);
                    return false;
                }

                await _log.WriteLog("Patch Stop", $"Application {processName} stopped successfully");
                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Stop Error", $"{ex}", 3);
                return false;
            }
        }

        private async Task<string> CreateBackupAsync(CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(_backupRoot);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFolder = Path.Combine(_backupRoot, $"Backup_{timestamp}");

                await _log.WriteLog("Patch Backup", $"Creating backup: {backupFolder}");

                Directory.CreateDirectory(backupFolder);

                // Copy all files recursively
                await CopyDirectoryAsync(_appFolder, backupFolder, cancellationToken);

                var fileCount = Directory.GetFiles(backupFolder, "*", SearchOption.AllDirectories).Length;
                await _log.WriteLog("Patch Backup", $"Backed up {fileCount} files");

                return backupFolder;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Backup Error", $"{ex}", 3);
                return null;
            }
        }

        //private async Task<bool> ApplyUpdateAsync(CancellationToken cancellationToken)
        //{
        //    try
        //    {
        //        await _log.WriteLog("Patch Update", "Removing old files");

        //        // Delete all files and subdirectories
        //        var files = Directory.GetFiles(_appFolder, "*", SearchOption.AllDirectories);
        //        foreach (var file in files)
        //        {
        //            cancellationToken.ThrowIfCancellationRequested();
        //            File.Delete(file);
        //        }

        //        var directories = Directory.GetDirectories(_appFolder);
        //        foreach (var dir in directories)
        //        {
        //            cancellationToken.ThrowIfCancellationRequested();
        //            Directory.Delete(dir, true);
        //        }

        //        await _log.WriteLog("Patch Update", "Copying new files");

        //        // Copy new files
        //        await CopyDirectoryAsync(_updateRoot, _appFolder, cancellationToken);

        //        var newFileCount = Directory.GetFiles(_appFolder, "*", SearchOption.AllDirectories).Length;
        //        await _log.WriteLog("Patch Update", $"Copied {newFileCount} files");

        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        await _log.WriteLog("Patch Update Error", $"{ex}", 3);
        //        return false;
        //    }
        //}

        //private async Task<bool> StartApplicationAsync(CancellationToken cancellationToken)
        //{
        //    try
        //    {
        //        string appPath = Path.Combine(_appFolder, _appName);

        //        if (!File.Exists(appPath))
        //        {
        //            await _log.WriteLog("Patch Start Error", $"Application not found: {appPath}", 3);
        //            return false;
        //        }

        //        await _log.WriteLog("Patch Start", $"Starting application: {appPath}");

        //        var psi = new ProcessStartInfo
        //        {
        //            FileName = appPath,
        //            WorkingDirectory = _appFolder,
        //            UseShellExecute = true
        //        };

        //        Process.Start(psi);

        //        // Wait for startup
        //        await Task.Delay(3000, cancellationToken);

        //        // Check if running
        //        var isRunning = Process.GetProcessesByName(_processName).Length > 0;

        //        if (isRunning)
        //        {
        //            await _log.WriteLog("Patch Start", "Application started successfully");
        //            return true;
        //        }
        //        else
        //        {
        //            await _log.WriteLog("Patch Start Error", "Application failed to start", 3);
        //            return false;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        await _log.WriteLog("Patch Start Error", ex.Message, 3);
        //        return false;
        //    }
        //}

        //private async Task<bool> VerifyApplicationAsync(CancellationToken cancellationToken)
        //{
        //    try
        //    {
        //        // Wait a bit more to ensure app is stable
        //        await Task.Delay(2000, cancellationToken);

        //        var processes = Process.GetProcessesByName(_processName);

        //        if (processes.Length == 0)
        //        {
        //            await _log.WriteLog("Patch Verify Error", "Application not running", 3);
        //            return false;
        //        }

        //        // Check if process is responsive (not crashed/hung)
        //        var process = processes[0];
        //        if (process.Responding)
        //        {
        //            await _log.WriteLog("Patch Verify", "Application verified and responsive");
        //            return true;
        //        }
        //        else
        //        {
        //            await _log.WriteLog("Patch Verify Error", "Application not responding", 3);
        //            return false;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        await _log.WriteLog("Patch Verify Error", ex.Message, 3);
        //        return false;
        //    }
        //}


        //private async Task RollbackAsync(string backupPath, CancellationToken cancellationToken)
        //{
        //    try
        //    {
        //        if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
        //        {
        //            await _log.WriteLog("Patch Rollback Error", "No backup available", 3);
        //            return;
        //        }

        //        await _log.WriteLog("Patch Rollback", $"Rolling back from: {backupPath}");

        //        // Stop application
        //        await StopApplicationAsync();

        //        // Delete current files
        //        var files = Directory.GetFiles(_appFolder, "*", SearchOption.AllDirectories);
        //        foreach (var file in files)
        //        {
        //            File.Delete(file);
        //        }

        //        var directories = Directory.GetDirectories(_appFolder);
        //        foreach (var dir in directories)
        //        {
        //            Directory.Delete(dir, true);
        //        }

        //        // Restore from backup
        //        await CopyDirectoryAsync(backupPath, _appFolder, cancellationToken);

        //        // Start application
        //        await StartApplicationAsync(cancellationToken);

        //        await _log.WriteLog("Patch Rollback", "Rollback completed");
        //    }
        //    catch (Exception ex)
        //    {
        //        await _log.WriteLog("Patch Rollback Error", ex.Message, 3);
        //    }
        //}

        private async Task<bool> ApplyUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _log.WriteLog("Patch Update", "Starting smart file replacement");

                int filesReplaced = 0;
                int filesAdded = 0;
                int filesDeleted = 0;

                // Get all files from update package
                var updateFiles = Directory.GetFiles(_updateRoot, "*", SearchOption.AllDirectories)
                    .Select(f => new
                    {
                        FullPath = f,
                        RelativePath = Path.GetRelativePath(_updateRoot, f)
                    })
                    .ToList();

                await _log.WriteLog("Patch Update", $"Found {updateFiles.Count} files in update package");

                // Copy/Replace files from update package
                foreach (var updateFile in updateFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string destPath = Path.Combine(_appFolder, updateFile.RelativePath);
                    string destDir = Path.GetDirectoryName(destPath);

                    // Create directory if needed
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Check if file exists
                    bool fileExists = File.Exists(destPath);

                    // Copy file (overwrite if exists)
                    File.Copy(updateFile.FullPath, destPath, overwrite: true);

                    if (fileExists)
                    {
                        filesReplaced++;
                    }
                    else
                    {
                        filesAdded++;
                    }
                }

                await _log.WriteLog("Patch Update",
                    $"Update completed: {filesReplaced} replaced, {filesAdded} added, {filesDeleted} deleted");

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Update Error", $"{ex}", 3);
                return false;
            }
        }
        private async Task<bool> StartApplicationAsync(CancellationToken cancellationToken)
        {
            try
            {
                string appPath = Path.Combine(_appFolder, _appName);

                if (!File.Exists(appPath))
                {
                    await _log.WriteLog("Patch Start Error", $"Application not found: {appPath}", 3);
                    return false;
                }

                await _log.WriteLog("Patch Start", $"Application updated: {appPath}");

                // ✅ DON'T restart here - let the main flow complete first
                await _log.WriteLog("Patch Start", "Application files updated successfully");

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Start Error", $"{ex}", 3);
                return false;
            }
        }

        private async Task<bool> VerifyApplicationAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Just verify the file exists
                string appPath = Path.Combine(_appFolder, _appName);

                if (File.Exists(appPath))
                {
                    await _log.WriteLog("Patch Verify", $"✓ Application file verified: {_appName}");
                    return true;
                }
                else
                {
                    await _log.WriteLog("Patch Verify Error", $"Application file not found: {appPath}", 3);
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Verify Error", $"{ex}", 3);
                return false;
            }
        }


        private async Task ScheduleSystemRestartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _log.WriteLog("Patch Restart", "Scheduling system restart...");

                string scriptPath = Path.Combine(Path.GetTempPath(), "restart_after_patch.ps1");
                string serviceName = _config["ServiceName"] ?? "MCS_BranchService";

                string scriptContent = $@"
# ✅ Wait longer to ensure all MQTT messages are delivered
Start-Sleep -Seconds 5

# Stop the service gracefully
Write-Output 'Stopping service: {serviceName}'
Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue

# Wait for service to stop completely
Start-Sleep -Seconds 3

# Restart computer
Write-Output 'Restarting computer now...'
shutdown.exe /r /t 1 /c 'Application update completed. Restarting...'
";

                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

                await _log.WriteLog("Patch Restart", "✓ Restart script created - executing in 5 seconds...");

                // Execute PowerShell script in background (detached)
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);

                await _log.WriteLog("Patch Restart", "✓ Restart scheduled - service will stop in 5 seconds");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Restart Error", $"{ex}", 3);
            }
        }

        private async Task RollbackAsync(string backupPath, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                {
                    await _log.WriteLog("Patch Rollback Error", "No backup available", 3);
                    return;
                }

                await _log.WriteLog("Patch Rollback", $"Rolling back from: {backupPath}");

                // Stop application (if running)
                await StopApplicationAsync(_processName);
                await StopApplicationAsync(_SecprocessName);

                // Delete current files
                var files = Directory.GetFiles(_appFolder, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    File.Delete(file);
                }

                var directories = Directory.GetDirectories(_appFolder);
                foreach (var dir in directories)
                {
                    Directory.Delete(dir, true);
                }

                // Restore from backup
                await CopyDirectoryAsync(backupPath, _appFolder, cancellationToken);

                await _log.WriteLog("Patch Rollback", "Rollback completed - forcing system restart...");

                //  CREATE POWERSHELL SCRIPT TO STOP SERVICE AND RESTART
                string scriptPath = Path.Combine(Path.GetTempPath(), "restart_after_rollback.ps1");
                string serviceName = _config["ServiceName"] ?? "MCS_BranchService";

                string scriptContent = $@"
# Stop the service gracefully
Write-Output 'Stopping service: {serviceName}'
Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue

# Wait for service to stop
Start-Sleep -Seconds 3

# Restart computer
Write-Output 'Restarting computer after rollback...'
shutdown.exe /r /t 1 /c 'Application rollback completed. Restarting...'
";

                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

                await _log.WriteLog("Patch Rollback", "Executing graceful service stop + restart...");

                // Execute PowerShell script
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);

                await _log.WriteLog("Patch Rollback", "Service stop + restart initiated");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Rollback Error", $"{ex}", 3);
            }
        }



        private async Task CleanupAsync(string downloadedZip)
        {
            try
            {
                // Delete downloaded ZIP
                if (File.Exists(downloadedZip))
                {
                    File.Delete(downloadedZip);
                    await _log.WriteLog("Patch Cleanup", "Deleted downloaded ZIP");
                }

                // Clean old backups (keep last N)
                if (Directory.Exists(_backupRoot))
                {
                    var backups = new DirectoryInfo(_backupRoot)
                        .GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime)
                        .Skip(_maxBackupsToKeep)
                        .ToList();

                    foreach (var backup in backups)
                    {
                        backup.Delete(true);
                    }

                    if (backups.Count > 0)
                    {
                        await _log.WriteLog("Patch Cleanup", $"Removed {backups.Count} old backup(s)");
                    }
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLog("Patch Cleanup Error", $"{ex}", 2);
            }
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destDir);

            // Copy files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // Copy subdirectories
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                await CopyDirectoryAsync(dir, destSubDir, cancellationToken);
            }
        }

        private async Task PublishStatusAsync(
     string UserId,
     string PatchId,
     PatchRequestType PatchRequestType,
     string branchId,
     PatchStatus status,
     PatchStep step,
     string message,
     int progress,
     CancellationToken cancellationToken)
        {
            var payload = new PatchStatusUpdateMqttResponse
            {
                UserId = UserId,
                PatchId = PatchId,
                PatchRequestType = PatchRequestType,
                Status = status,
                Step = step,
                Message = message,
                Progress = progress,
                Timestamp = DateTime.Now
            };

            // Determine if this is a critical status that needs QoS 2
            bool isCritical = status == PatchStatus.SUCCESS ||
                              status == PatchStatus.FAILED ||
                              status == PatchStatus.ROLLBACK;

            MqttQualityOfServiceLevel qosLevel;
            bool retain;

            if (isCritical)
            {
                qosLevel = MqttQualityOfServiceLevel.ExactlyOnce;
                retain = true;
            }
            else
            {
                qosLevel = MqttQualityOfServiceLevel.AtLeastOnce;
                retain = false;
            }

            int maxRetries = isCritical ? 3 : 1;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    await _mqtt.PublishAsync(
                        $"server/{branchId}/PATCH/Status",
                        payload,
                        qosLevel,
                        cancellationToken,
                        retain);

                    await _log.WriteLog("MQTT Status", $"✓ Published: {step} ({status}) - QoS {qosLevel}");
                    return; // Success
                }
                catch (Exception ex)
                {
                    retryCount++;
                    await _log.WriteLog("MQTT Publish Error", $"Attempt {retryCount}/{maxRetries}: {ex.Message}", 2);

                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(500 * retryCount, cancellationToken); // Exponential backoff
                    }
                    else
                    {
                        await _log.WriteLog("MQTT Publish Failed", $"Failed after {maxRetries} attempts: {step} ({status})", 3);
                    }
                }
            }
        }
    }
}