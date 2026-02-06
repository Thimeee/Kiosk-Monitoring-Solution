using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Monitoring.Shared.DTO;

namespace SFTPService.Service
{
    public interface IPerformanceService
    {
        Task<PerformanceInfo> GetPerformanceAsync();
    }

    public class PerformanceService : IPerformanceService, IDisposable
    {
        private readonly ILogger<PerformanceService> _logger;
        private readonly PerformanceCounter _cpuCounter;
        private double _lastCpuValue;
        private DateTime _lastCpuRead = DateTime.MinValue;
        private readonly SemaphoreSlim _cpuLock = new(1, 1);
        private bool _disposed;

        private const int CPU_CACHE_SECONDS = 1;
        private const double BYTES_TO_KB = 1024.0;
        private const double BYTES_TO_MB = 1024.0 * 1024.0;
        private const double BYTES_TO_GB = 1024.0 * 1024.0 * 1024.0;

        public PerformanceService(ILogger<PerformanceService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue(); // Initialize - first reading is always 0
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize CPU performance counter");
                }
            }
        }

        public async Task<PerformanceInfo> GetPerformanceAsync()
        {
            try
            {
                var cpu = await GetCpuInfoAsync();
                var ram = GetRamInfo();
                var disk = GetDiskInfo();
                var network = GetNetworkInfo();

                return new PerformanceInfo
                {
                    Cpu = cpu,
                    Ram = ram,
                    Disk = disk,
                    Network = network
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving performance information");
                throw;
            }
        }

        private async Task<CpuInfo> GetCpuInfoAsync()
        {
            double cpuUsage = 0;

            if (OperatingSystem.IsWindows() && _cpuCounter != null)
            {
                await _cpuLock.WaitAsync();
                try
                {
                    var now = DateTime.UtcNow;

                    // Cache CPU reading to avoid performance overhead
                    if ((now - _lastCpuRead).TotalSeconds >= CPU_CACHE_SECONDS)
                    {
                        _lastCpuValue = _cpuCounter.NextValue();
                        _lastCpuRead = now;
                    }

                    cpuUsage = _lastCpuValue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read CPU usage");
                }
                finally
                {
                    _cpuLock.Release();
                }
            }

            var process = Process.GetCurrentProcess();
            var upTime = TimeSpan.Zero;

            try
            {
                upTime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate process uptime");
            }

            return new CpuInfo
            {
                CpuUsagePercent = Math.Round(cpuUsage, 2),
                ProcessCount = Process.GetProcesses().Length,
                UpTime = FormatUptime(upTime),
                LogicalProcessors = Environment.ProcessorCount,
                VirtualizationEnabled = OperatingSystem.IsWindows()
            };
        }

        private RamInfo GetRamInfo()
        {
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogWarning("RAM info only supported on Windows");
                return null;
            }

            try
            {
                var ramInfo = new RamInfo();

                // OS Memory Information
                GetOsMemoryInfo(ramInfo);

                ;

                // Physical RAM Details
                GetPhysicalRamInfo(ramInfo);



                return ramInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve RAM information");
                return null;
            }
        }

        private void GetOsMemoryInfo(RamInfo ramInfo)
        {
            using var osSearcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize FROM Win32_OperatingSystem");

            foreach (ManagementObject os in osSearcher.Get())
            {
                var totalKb = Convert.ToDouble(os["TotalVisibleMemorySize"]);
                var freeKb = Convert.ToDouble(os["FreePhysicalMemory"]);
                var virtualKb = Convert.ToDouble(os["TotalVirtualMemorySize"]);

                ramInfo.TotalGB = Math.Round(totalKb / BYTES_TO_MB, 2);
                ramInfo.AvailableGB = Math.Round(freeKb / BYTES_TO_MB, 2);
                ramInfo.InUseGB = Math.Round(ramInfo.TotalGB - ramInfo.AvailableGB, 2);
                ramInfo.Committed = $"{Math.Round((virtualKb - freeKb) / BYTES_TO_MB, 1)} / {Math.Round(virtualKb / BYTES_TO_MB, 1)} GB";
            }
        }



        private void GetPhysicalRamInfo(RamInfo ramInfo)
        {
            using var ramSearcher = new ManagementObjectSearcher(
                "SELECT Speed, FormFactor FROM Win32_PhysicalMemory");

            var sticks = ramSearcher.Get().Cast<ManagementObject>().ToList();

            if (sticks.Any())
            {
                ramInfo.SlotsUsed = $"{sticks.Count} of {sticks.Count}";
            }
        }

        private DiskInfo GetDiskInfo()
        {
            try
            {
                var drive = DriveInfo.GetDrives()
                    .FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);

                if (drive == null)
                {
                    _logger.LogWarning("No ready fixed drive found");
                    return null;
                }

                var (driveType, ssdType) = GetDriveTypeInfo();

                return new DiskInfo
                {
                    Name = drive.Name,
                    DriveType = driveType,
                    SSDType = ssdType,
                    CapacityGB = Math.Round(drive.TotalSize / BYTES_TO_GB, 2),
                    FreeSpaceGB = Math.Round(drive.TotalFreeSpace / BYTES_TO_GB, 2)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve disk information");
                return null;
            }
        }

        private (string driveType, string ssdType) GetDriveTypeInfo()
        {
            if (!OperatingSystem.IsWindows())
                return ("Unknown", "N/A");

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"ROOT\Microsoft\Windows\Storage",
                    "SELECT MediaType, BusType FROM MSFT_PhysicalDisk");

                foreach (ManagementObject disk in searcher.Get())
                {
                    var mediaType = Convert.ToInt32(disk["MediaType"]);
                    var busType = Convert.ToInt32(disk["BusType"]);

                    if (mediaType == 4) // SSD
                    {
                        var ssdType = busType switch
                        {
                            17 => "NVMe",
                            11 => "SATA",
                            _ => "SSD"
                        };
                        return ("SSD", ssdType);
                    }

                    if (mediaType == 3) // HDD
                        return ("HDD", "N/A");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine drive type");
            }

            return ("Unknown", "N/A");
        }

        private NetworkInfo GetNetworkInfo()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                       n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                if (nic == null)
                {
                    _logger.LogWarning("No active network interface found");
                    return new NetworkInfo();
                }

                var ipv4Statistics = nic.GetIPv4Statistics();
                var (ssid, signalStrength) = (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && OperatingSystem.IsWindows())
                    ? GetWiFiInfo()
                    : (null, 0);

                return new NetworkInfo
                {
                    AdapterName = nic.Name,
                    SSID = ssid,
                    ConnectionType = nic.NetworkInterfaceType.ToString(),
                    IPv4 = GetIPv4Address(nic),
                    IPv6 = GetIPv6Address(nic),
                    SendKbps = Math.Round(ipv4Statistics.BytesSent / BYTES_TO_KB, 2),
                    ReceiveKbps = Math.Round(ipv4Statistics.BytesReceived / BYTES_TO_KB, 2),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve network information");
                return new NetworkInfo();
            }
        }

        private static string GetIPv4Address(NetworkInterface nic)
        {
            return nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.Address.ToString();
        }

        private static string GetIPv6Address(NetworkInterface nic)
        {
            return nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                ?.Address.ToString();
        }

        private (string ssid, int signal) GetWiFiInfo()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show interfaces",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var ssid = ExtractWiFiValue(output, "SSID");
                var signalStr = ExtractWiFiValue(output, "Signal")?.Replace("%", "");
                var signal = int.TryParse(signalStr, out var s) ? s : 0;

                return (ssid, signal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve WiFi information");
                return (null, 0);
            }
        }

        private static string ExtractWiFiValue(string output, string key)
        {
            var line = output.Split(Environment.NewLine)
                .FirstOrDefault(l => l.Trim().StartsWith(key, StringComparison.OrdinalIgnoreCase));

            return line?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        }

        private static string FormatUptime(TimeSpan upTime)
        {
            return $"{(int)upTime.TotalHours}:{upTime.Minutes:D2}:{upTime.Seconds:D2}";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cpuCounter?.Dispose();
                _cpuLock?.Dispose();
            }

            _disposed = true;
        }
    }
}