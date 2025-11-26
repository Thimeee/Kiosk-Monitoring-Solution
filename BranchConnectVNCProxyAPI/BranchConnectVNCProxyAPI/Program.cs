using System.Net.Sockets;
using NoVncBlazor.Server;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddScoped<VncWebSocketProxy>();


// Configure CORS for Blazor client
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//  CORS for Blazor client
app.UseCors("AllowBlazorClient");

// UseWebSockets use
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
});

// Diagnostic endpoint to test VNC connectivity
app.MapGet("/test-vnc", async (string host = "localhost", int port = 5900) =>
{
    try
    {
        using var tcpClient = new TcpClient();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tcpClient.ConnectAsync(host, port, cts.Token);

        if (tcpClient.Connected)
        {
            return Results.Ok(new
            {
                success = true,
                message = $"Successfully connected to VNC server at {host}:{port}",
                host,
                port
            });
        }

        return Results.Problem($"Failed to connect to VNC server at {host}:{port}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error connecting to VNC: {ex.Message}");
    }
});

// WebSocket endpoint for VNC proxy
app.Map("/vnc-proxy", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        // Get VNC server details from query string
        var vncHost = context.Request.Query["host"].ToString();
        var vncPortStr = context.Request.Query["port"].ToString();

        if (string.IsNullOrEmpty(vncHost))
        {
            vncHost = "localhost";
        }

        if (!int.TryParse(vncPortStr, out int vncPort))
        {
            vncPort = 5900; // Default VNC port
        }



        // Accept WebSocket with the 'binary' subprotocol that noVNC uses
        var requestedProtocols = context.WebSockets.WebSocketRequestedProtocols;
        string? subProtocol = null;

        if (requestedProtocols.Contains("binary"))
        {
            subProtocol = "binary";
        }

        var webSocket = await context.WebSockets.AcceptWebSocketAsync(
            new WebSocketAcceptContext
            {
                SubProtocol = subProtocol
            });

        var proxy = context.RequestServices.GetRequiredService<VncWebSocketProxy>();

        await proxy.HandleWebSocketAsync(webSocket, vncHost, vncPort);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection required");
    }
});


//app.UseHttpsRedirection();

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

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
