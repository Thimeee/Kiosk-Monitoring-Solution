using System.Configuration;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;
using SFTPService;
//using SFTPService.Helper;
using SFTPService.Service;
//using SFTPService.Services;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        //services.AddSingleton<RabbitHelper>();
        // Bind JSON → AppConfig
        services.Configure<AppConfig>(configuration);

        services.AddSingleton<SftpFileService>();
        services.AddSingleton<LoggerService>();
        services.AddSingleton<MQTTHelper>();
        services.AddSingleton<CDKApplctionStatusService>();
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddSingleton<IPatchService, PatchService>();
        services.AddSingleton<SqliteService>();
        services.Configure<AppConfig>(configuration);
        services.AddHostedService<GracefulStartup>();
        services.AddHostedService<Worker>();
    })
    .UseWindowsService(); // FIXED: Call on IHostBuilder, not ConfigureHostBuilder

await builder.Build().RunAsync();