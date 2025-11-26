using System;
using System.IO;
using System.Threading.Tasks;

namespace TestAPINoVNC
{
    public class VncProxyFileLogger
    {
        // Log folder inside project root
        private string baseLogDir = Path.Combine(AppContext.BaseDirectory, "Logs");

        // Subfolders for each log type
        private string delayLogFolder => Path.Combine(baseLogDir, "ApplicationDealyLog");
        private string initialLogFolder => Path.Combine(baseLogDir, "ApplicationInitialLog");
        private string exceptionLogFolder => Path.Combine(baseLogDir, "ApplicationExecptionLog");

        private const int LogRetentionDays = 20; // Keep logs for 20 days

        public async Task WriteLog(string logType, string message, int logWritenType = 1)
        {
            string logFolder = GetLogFolder(logWritenType);
            if (!Directory.Exists(logFolder))
                Directory.CreateDirectory(logFolder);

            string logFilePath = GetDailyLogFilePath(logFolder);

            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logType}] {message}{Environment.NewLine}";

            try
            {
                await File.AppendAllTextAsync(logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write log: {ex.Message}");
            }

            // Clean old logs in all log folders
            CleanOldLogs();
        }

        private string GetLogFolder(int logWritenType)
        {
            return logWritenType switch
            {
                1 => delayLogFolder,
                2 => initialLogFolder,
                3 => exceptionLogFolder,
                _ => delayLogFolder
            };
        }

        private string GetDailyLogFilePath(string folder)
        {
            string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".txt"; // Daily file
            return Path.Combine(folder, fileName);
        }

        private void CleanOldLogs()
        {
            string[] logFolders = { delayLogFolder, initialLogFolder, exceptionLogFolder };

            foreach (var folder in logFolders)
            {
                if (!Directory.Exists(folder))
                    continue;

                try
                {
                    var files = Directory.GetFiles(folder, "*.txt");
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < DateTime.Now.AddDays(-LogRetentionDays))
                            File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }
    }
}
