using System;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Shared.DTO;
using MonitoringBackend.Data;
using MonitoringBackend.DTO;
using MonitoringBackend.Helper;
using MonitoringBackend.Service;
using MonitoringBackend.SRHub;
using SFTPService.Helper;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorDev",
        policy => policy
            .WithOrigins(allowedOrigins) // Blazor WASM URL
            .AllowAnyHeader()
            .AllowAnyMethod()
    .AllowCredentials());
});

// Configure Kestrel to support HTTP/2 for gRPC over plain HTTP
//builder.WebHost.ConfigureKestrel(options =>
//{
//    options.ListenLocalhost(5155, listenOptions =>
//    {
//        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
//    });
//});
//Use https 
//builder.WebHost.ConfigureKestrel(options =>
//{
//    options.ListenAnyIP(5155, listenOptions =>
//    {
//        listenOptions.UseHttps("certs/server.pfx", "YourCertPassword");
//        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
//    });
//});



//use production level 

//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("BlazorWithJwt",
//        policy => policy
//            .WithOrigins("https://myapp.bank.lk")  // your Blazor WASM production URL
//            .WithHeaders("Authorization", "Content-Type") // allow JWT + JSON headers
//            .WithMethods("GET", "POST", "PUT", "DELETE") // only allow required methods
//            .AllowCredentials()); // needed if using cookies (optional)
//});
//use production level 

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue; // Allow very large uploads
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();


// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters

    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    }
    ;
});
// JWT Authentication


builder.Services.AddControllers();
builder.Services.AddAuthorization();

builder.Services.AddSignalR();
builder.Services.AddSingleton<SftpStorageService>();
builder.Services.AddSingleton<LoggerService>();
builder.Services.AddSingleton<MQTTHelper>();
builder.Services.AddSingleton<GetFolderStructure>();
builder.Services.AddHostedService<MqttWorker>();
//builder.Services.AddHostedService<RabbitSubscriberService>();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//void SeedRoles(IServiceProvider services)
//{
//    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
//    string[] roles = { "Admin", "Manager" };

//    foreach (var role in roles)
//    {
//        if (!roleManager.RoleExistsAsync(role).Result)
//            roleManager.CreateAsync(new IdentityRole(role)).Wait();
//    }
//}



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//SeedRoles(app.Services.CreateScope().ServiceProvider);

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();


app.UseCors("AllowBlazorDev");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BranchHub>("/branchHub");
//app.MapGrpcService<BranchMonitorService>();

//// RabbitMQ Subscriptions
//var rabbit = app.Services.GetRequiredService<RabbitHelper>();
//await rabbit.RabbitHelperInit("192.168.1.13", "branchuser", "StrongPass123");
////var statusService = app.Services.GetRequiredService<StatusService>();
////var hubContext = app.Services.GetRequiredService<IHubContext<StatusHub>>();

//await rabbit.SubscribeToStatusOneBranchPatch(async msg =>
//{
//    var s = JsonSerializer.Deserialize<ResponseStatus>(msg);

//    FolderNode jsonMessage = JsonSerializer.Deserialize<FolderNode>(s.resposeMassage);

//    var g = jsonMessage;

//    // ---- PUSH LIVE UPDATE TO UI ----
//    //await hubContext.Clients.All.SendAsync("BranchStatusUpdated", s);
//});




app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

