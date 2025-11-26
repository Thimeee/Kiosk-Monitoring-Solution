using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using Monitoring.Shared.DTO;

namespace SFTPService.Helper
{
    public interface IPerformanceService
    {
        Task<PerformanceInfo> GetPerformanceAsync();
    }

    public class PerformanceService : IPerformanceService
    {
        private PerformanceCounter _cpuCounter;

        public PerformanceService()
        {
            if (OperatingSystem.IsWindows())
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // first reading is always 0
            }
        }

        public async Task<PerformanceInfo> GetPerformanceAsync()
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

        public async Task<CpuInfo> GetCpuInfoAsync()
        {
            double cpu = 0;
            if (OperatingSystem.IsWindows())
            {
                cpu = _cpuCounter.NextValue();
                await Task.Delay(1000);
                cpu = _cpuCounter.NextValue();
            }

            var process = Process.GetCurrentProcess();
            TimeSpan upTime = TimeSpan.Zero;
            try
            {
                var startTime = process.StartTime;
                upTime = DateTime.Now - startTime;
            }
            catch { }

            return new CpuInfo
            {
                CpuUsagePercent = Math.Round(cpu, 2),
                ProcessCount = Process.GetProcesses().Length,
                ThreadCount = process.Threads.Count,
                HandleCount = OperatingSystem.IsWindows() ? process.HandleCount : 0,
                UpTime = $"{(int)upTime.TotalHours}:{upTime.Minutes:D2}:{upTime.Seconds:D2}",
                Cores = Environment.ProcessorCount,
                LogicalProcessors = Environment.ProcessorCount,
                VirtualizationEnabled = OperatingSystem.IsWindows() ? true : false,
            };
        }

        public RamInfo GetRamInfo()
        {
            double totalRam = GetTotalRamGB();
            double availableRam = GetAvailableRamGB();
            double usedRam = totalRam - availableRam;

            return new RamInfo
            {
                InUseGB = Math.Round(usedRam, 2),
                CommittedGB = Math.Round(totalRam, 2),
                AvailableGB = Math.Round(availableRam, 2)
            };
        }

        public double GetTotalRamGB()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (var obj in searcher.Get())
                    {
                        var totalKb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        return Math.Round(totalKb / 1024 / 1024, 2);
                    }
                }
            }
            catch { }
            return 0;
        }

        public double GetAvailableRamGB()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (var obj in searcher.Get())
                    {
                        var availKb = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        return Math.Round(availKb / 1024 / 1024, 2);
                    }
                }
            }
            catch { }
            return 0;
        }

        public DiskInfo GetDiskInfo()
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady) ?? DriveInfo.GetDrives().First();
            double used = drive.TotalSize - drive.TotalFreeSpace;

            return new DiskInfo
            {
                Name = drive.Name,
                CapacityGB = Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 2),
                FreeSpaceGB = Math.Round(drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0, 2),
            };
        }



        public NetworkInfo GetNetworkInfo()
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                             n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            string adapterName = nic?.Name;
            string ipv4 = nic?.GetIPProperties().UnicastAddresses
                               .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                               ?.Address.ToString();
            string ipv6 = nic?.GetIPProperties().UnicastAddresses
                               .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                               ?.Address.ToString();
            string connectionType = nic?.NetworkInterfaceType.ToString();

            double sendKbps = nic?.GetIPv4Statistics().BytesSent / 1024.0 ?? 0;
            double receiveKbps = nic?.GetIPv4Statistics().BytesReceived / 1024.0 ?? 0;

            string ssid = null;
            int signal = 0;

            if (OperatingSystem.IsWindows() && nic?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                var processNetsh = new Process
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
                processNetsh.Start();
                string output = processNetsh.StandardOutput.ReadToEnd();
                processNetsh.WaitForExit();

                var ssidLine = output.Split(Environment.NewLine)
                                     .FirstOrDefault(l => l.Trim().StartsWith("SSID"));
                if (ssidLine != null)
                    ssid = ssidLine.Split(":", 2)[1].Trim();

                var signalLine = output.Split(Environment.NewLine)
                                       .FirstOrDefault(l => l.Trim().StartsWith("Signal"));
                if (signalLine != null && int.TryParse(signalLine.Split(":", 2)[1].Trim().Replace("%", ""), out int s))
                    signal = s;
            }

            return new NetworkInfo
            {
                AdapterName = adapterName,
                SSID = ssid,
                ConnectionType = connectionType,
                IPv4 = ipv4,
                IPv6 = ipv6,
                SendKbps = Math.Round(sendKbps, 2),
                ReceiveKbps = Math.Round(receiveKbps, 2),
                SignalStrength = signal
            };
        }
    }
}
