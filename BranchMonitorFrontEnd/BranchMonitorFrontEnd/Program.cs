using BranchMonitorFrontEnd;
using BranchMonitorFrontEnd.Helper;
using BranchMonitorFrontEnd.Service.Alert;
using BranchMonitorFrontEnd.Service.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using static BranchMonitorFrontEnd.Layout.NavMenu;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");


builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddBlazorBootstrap();

// Register the alert Service
builder.Services.AddScoped<AlertService>();

// Register the LayoutSharedService as a singleton service
builder.Services.AddSingleton<LayoutSharedService>();

// Register the custom jwt authentication state provider
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped<AuthCredential>();

// Load appsettings.json from wwwroot
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
// Bind strongly typed class
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Register for DI
builder.Services.AddSingleton(appSettings);


await builder.Build().RunAsync();
