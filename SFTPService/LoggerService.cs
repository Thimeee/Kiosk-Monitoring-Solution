using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace SFTPService
{
    public class LoggerService
    {
        private readonly IConfiguration _config;

        public LoggerService(IConfiguration config)
        {
            _config = config;
        }


        public async Task WriteLog(string logType, string message, int logWritenType = 1)
        {
            string logFilePath = GetDailyLogFilePath(logWritenType);
            if (logFilePath != null)
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logType}] {message}{Environment.NewLine}";
                await Task.Run(() => File.AppendAllTextAsync(logFilePath, logEntry));
            }
        }

        private string GetDailyLogFilePath(int LogWritenType)
        {
            if (_config != null)
            {
                string? basePath;

                if (LogWritenType == 1)
                {
                    basePath = _config["LogPath1:PathUrl"];
                }
                else if (LogWritenType == 2)
                {
                    basePath = _config["LogPath2:PathUrl"];
                }
                else if (LogWritenType == 3)
                {
                    basePath = _config["LogPath3:PathUrl"];
                }
                else
                {
                    basePath = _config["LogPath1:PathUrl"];
                }

                if (string.IsNullOrWhiteSpace(basePath))
                    basePath = @"C:\Logs\application.log";

                string logDir = Path.GetDirectoryName(basePath) ?? @"C:\Logs";
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string fileName = Path.GetFileNameWithoutExtension(basePath);
                string extension = Path.GetExtension(basePath);
                string dateSuffix = DateTime.Now.ToString("yyyy-MM-dd");
                string dailyFileName = $"{fileName}-{dateSuffix}{extension}";

                return Path.Combine(logDir, dailyFileName);
            }
            else
            {
                return null;
            }
        }
    }
}
