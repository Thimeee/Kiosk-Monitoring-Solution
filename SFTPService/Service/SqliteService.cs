using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;

namespace SFTPService.Service;

public class SqliteService
{
    private readonly string _connectionString;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly AppConfig _config;

    public SqliteService(IOptions<AppConfig> config)
    {
        _config = config.Value;

        var root = _config.MainPath.PathUrl;

        var dbRelative = _config.SubPath.Db;

        var folder = Path.Combine(root, dbRelative);
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "monitoring.db");

        if (!File.Exists(dbPath))
            using (File.Create(dbPath)) { }

        _connectionString = $"Data Source={dbPath};Cache=Shared";
        InitializeDb();
    }

    private void InitializeDb()
    {
        using var con = new SqliteConnection(_connectionString);
        con.Open();

        con.Execute("PRAGMA journal_mode=WAL;");
        con.Execute("PRAGMA synchronous=NORMAL;");

        // Example table
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

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task<long> InsertAsync<T>(string sql, T data)
    {
        await _lock.WaitAsync();
        try
        {
            using var con = CreateConnection();
            return await con.ExecuteScalarAsync<long>(sql, data);
        }
        finally { _lock.Release(); }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
    {
        using var con = CreateConnection();
        return await con.QueryAsync<T>(sql, param);
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, object? param = null)
    {
        using var con = CreateConnection();
        return await con.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task<int> UpdateAsync(string sql, object data)
    {
        await _lock.WaitAsync();
        try
        {
            using var con = CreateConnection();
            return await con.ExecuteAsync(sql, data);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> DeleteAsync(string sql, object? param = null)
    {
        await _lock.WaitAsync();
        try
        {
            using var con = CreateConnection();
            return await con.ExecuteAsync(sql, param);
        }
        finally { _lock.Release(); }
    }
}