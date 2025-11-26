using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class CpuInfo
    {
        public double CpuUsagePercent { get; set; }
        public int ProcessCount { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public string UpTime { get; set; }
        public double BaseSpeedGHz { get; set; }
        public int Sockets { get; set; }
        public int Cores { get; set; }
        public int LogicalProcessors { get; set; }
        public bool VirtualizationEnabled { get; set; }
    }

    public class RamInfo
    {
        public double InUseGB { get; set; }
        public double CommittedGB { get; set; }
        public double AvailableGB { get; set; }
    }

    public class DiskInfo
    {
        public string Name { get; set; }
        public double CapacityGB { get; set; }
        public double FreeSpaceGB { get; set; }
    }

    public class NetworkInfo
    {
        public string AdapterName { get; set; }
        public string SSID { get; set; }
        public string ConnectionType { get; set; }
        public string IPv4 { get; set; }
        public string IPv6 { get; set; }
        public double SendKbps { get; set; }
        public double ReceiveKbps { get; set; }
        public int SignalStrength { get; set; } // 0-100%
    }

    public class PerformanceInfo
    {
        public CpuInfo Cpu { get; set; }
        public RamInfo Ram { get; set; }
        public DiskInfo Disk { get; set; }
        public NetworkInfo Network { get; set; }
    }

}
