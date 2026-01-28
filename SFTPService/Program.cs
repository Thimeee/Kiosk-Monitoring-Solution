using SFTPService;
using SFTPService.Helper;
//using SFTPService.Services;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        //services.AddSingleton<RabbitHelper>();
        services.AddSingleton<SftpFileService>();
        services.AddSingleton<LoggerService>();
        services.AddSingleton<MQTTHelper>();
        services.AddSingleton<CDKApplctionStatusService>();
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddSingleton<IPatchService, PatchService>();
        services.AddHostedService<Worker>();
    })
    .UseWindowsService(); // ✅ FIXED: Call on IHostBuilder, not ConfigureHostBuilder

await builder.Build().RunAsync();