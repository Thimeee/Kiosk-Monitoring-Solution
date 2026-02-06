using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO.WorkerServiceConfigDto
{
    public class AppConfig
    {
        public string BranchId { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public ConnectionStringsConfig ConnectionStrings { get; set; } = new();
        public SftpConfig Sftp { get; set; } = new();
        public MqttConfig MQTT { get; set; } = new();
        public ApiConfig API { get; set; } = new();
        public MainPathConfig MainPath { get; set; } = new();
        public SubPathConfig SubPath { get; set; } = new();
        public PatchConfig Patch { get; set; } = new();
        public LogConfig Log { get; set; } = new();
    }

    public class ConnectionStringsConfig
    {
        public string BranchDb { get; set; } = "";
    }

    public class SftpConfig
    {
        public string Host { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public int Port { get; set; } = 22;
    }

    public class MqttConfig
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 1883;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class ApiConfig
    {
        public string Host { get; set; } = "";
    }

    public class MainPathConfig
    {
        public string PathUrl { get; set; } = "";
    }

    public class SubPathConfig
    {
        public string Log { get; set; } = "";
        public string Patch { get; set; } = "";
        public string Db { get; set; } = "";
    }

    public class PatchConfig
    {
        public ApplicationPatchConfig ApplicationPatch { get; set; } = new();
    }

    public class ApplicationPatchConfig
    {
        public string MainAppName { get; set; } = "";
        public string SecondAppName { get; set; } = "";
        public string AppFolder { get; set; } = "";
        public string BackupRoot { get; set; } = "";
        public string UpdateRoot { get; set; } = "";
        public string DownloadsPath { get; set; } = "";
        public int MaxBackupsToKeep { get; set; } = 5;
    }

    public class LogConfig
    {
        public string DelayLog { get; set; } = "";
        public string InitialLog { get; set; } = "";
        public string ExceptionLog { get; set; } = "";
        public string ConnectionLog { get; set; } = "";
    }


}
