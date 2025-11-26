using SFTPService;
using SFTPService.Helper;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        //services.AddSingleton<RabbitHelper>();
        services.AddSingleton<SftpFileService>();
        services.AddSingleton<LoggerService>();
        services.AddSingleton<MQTTHelper>();
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddHostedService<Worker>();
    })
    .UseWindowsService(); // ✅ FIXED: Call on IHostBuilder, not ConfigureHostBuilder

await builder.Build().RunAsync();