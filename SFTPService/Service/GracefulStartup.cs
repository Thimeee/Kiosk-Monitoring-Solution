using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;

namespace SFTPService.Service;

public class GracefulStartup : IHostedService
{
    private readonly AppConfig _config;
    private readonly string _dbPath;

    public GracefulStartup(IOptions<AppConfig> config)
    {
        _config = config.Value;

        var root = _config.MainPath.PathUrl;

        var dbRelative = _config.SubPath.Db;

        // Ensure this is a folder
        var folder = Path.Combine(root, dbRelative);
        Directory.CreateDirectory(folder); // OK even if already exists

        _dbPath = Path.Combine(folder, "monitoring.db");

        // Create file only if it does not exist
        if (!File.Exists(_dbPath))
            using (File.Create(_dbPath)) { }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var root = _config.MainPath.PathUrl;

            // Create log folders
            var logPath = Path.Combine(root, _config.SubPath.Log);
            Create(Path.Combine(logPath, _config.Log.DelayLog));
            Create(Path.Combine(logPath, _config.Log.InitialLog));
            Create(Path.Combine(logPath, _config.Log.ExceptionLog));
            Create(Path.Combine(logPath, _config.Log.ConnectionLog));

            //Create Application patch folders
            var patchPath = Path.Combine(root, _config.SubPath.Patch);

            Create(Path.Combine(patchPath, _config.Patch.ApplicationPatch.BackupRoot));
            Create(Path.Combine(patchPath, _config.Patch.ApplicationPatch.UpdateRoot));
            Create(Path.Combine(patchPath, _config.Patch.ApplicationPatch.DownloadsPath));


            // Initialize SQLite DB
            InitializeDb();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Config failed: {ex.Message}");
            throw;
        }



        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Create(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private void InitializeDb()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        if (!File.Exists(_dbPath))
            File.Create(_dbPath).Dispose();

        using var con = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
        con.Open();

        con.Execute("PRAGMA journal_mode=WAL;");
        con.Execute("PRAGMA synchronous=NORMAL;");

        con.Execute("""
        CREATE TABLE IF NOT EXISTS ServiceConfig (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TerminalId TEXT NOT NULL,
            BranchId TEXT NOT NULL,
            Location TEXT NOT NULL,
            TerminalVersion TEXT NOT NULL,
            TerminalSeriNumber TEXT NOT NULL,  
            TerminalName TEXT NOT NULL,   
            FolderPath TEXT NOT NULL
        );
        """);
    }
}