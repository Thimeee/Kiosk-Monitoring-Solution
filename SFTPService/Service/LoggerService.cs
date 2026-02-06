using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;

namespace SFTPService.Service
{
    public class LoggerService
    {
        private readonly AppConfig _config;

        private static readonly SemaphoreSlim _lock = new(1, 1);

        public LoggerService(IOptions<AppConfig> config)
        {
            _config = config.Value;
        }

        public async Task WriteLogAsync(
            LogType logType,
            string level,
            string message)
        {
            var logFilePath = GetDailyLogFilePath(logType);
            var logEntry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

            try
            {
                await _lock.WaitAsync();

                await File.AppendAllTextAsync(
                    logFilePath,
                    logEntry,
                    Encoding.UTF8);
            }
            catch
            {
                // DO NOT throw from logger
                // Last-resort fallback can be added here
            }
            finally
            {
                _lock.Release();
            }
        }

        private string GetDailyLogFilePath(LogType logType)
        {
            string folder = logType switch
            {
                LogType.Delay => CombinePaths(_config.Log.DelayLog),
                LogType.Initial => CombinePaths(_config.Log.InitialLog),
                LogType.Exception => CombinePaths(_config.Log.ExceptionLog),
                LogType.Connection => CombinePaths(_config.Log.ConnectionLog),
                _ => CombinePaths(_config.Log.DelayLog)
            };

            Directory.CreateDirectory(folder);

            string fileName = $"{logType}-{DateTime.Now:yyyy-MM-dd}.log";
            return Path.Combine(folder, fileName);
        }

        private string CombinePaths(string logtype)
        {
            var root = _config.MainPath.PathUrl;
            var logPath = Path.Combine(root, _config.SubPath.Log!);

            return Path.Combine(logPath, logtype!);
        }
    }
}