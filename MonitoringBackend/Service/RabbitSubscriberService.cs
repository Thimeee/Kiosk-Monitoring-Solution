//using System.Text.Json;
//using Microsoft.AspNetCore.SignalR;
//using Monitoring.Shared.DTO;
//using MonitoringBackend.Helper;
//using MonitoringBackend.SRHub;
//using static System.Runtime.InteropServices.JavaScript.JSType;

//namespace MonitoringBackend.Service
//{
//    public class RabbitSubscriberService : BackgroundService
//    {
//        private readonly RabbitHelper _rabbit;
//        private readonly ILogger<RabbitSubscriberService> _logger;
//        private readonly IConfiguration _config;
//        private readonly IHubContext<BranchHub> _hubContext;

//        public RabbitSubscriberService(RabbitHelper rabbit, ILogger<RabbitSubscriberService> logger, IConfiguration configuration, IHubContext<BranchHub> hubContext)
//        {
//            _rabbit = rabbit;
//            _logger = logger;
//            _config = configuration;
//            _hubContext = hubContext;
//        }

//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {

//            var RabbitMQHost = _config["RabbitMQ:Host"];
//            var RabbitMQUser = _config["RabbitMQ:Username"];
//            var RabbitMQPort = _config["RabbitMQ:Port"];
//            var RabbitMQPW = _config["RabbitMQ:Password"];

//            // Initialize connection to RabbitMQ
//            await _rabbit.RabbitHelperInit(RabbitMQHost, RabbitMQUser, RabbitMQPW);

//            // Subscribe to messages
//            await _rabbit.SubscribeToStatusOneBranchPatch(async msg =>
//            {
//                try
//                {
//                    //var status = JsonSerializer.Deserialize<BranchJobResponse<FileDetails>>(msg);
//                    //_logger.LogInformation($"Received status message: {status?.resposeMassage}");
//                    //await _hubContext.Clients.All.SendAsync("BranchUpdate", msg);
//                    await _hubContext.Clients.All.SendAsync("BranchUpdate", msg);

//                    // Check if any clients are connected
//                    //var clientCount = _hubContext.Clients; // This will always be available

//                    //await _hubContext.Clients.All.SendAsync("BranchUpdate", msg, stoppingToken);


//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "Error processing RabbitMQ message");
//                }
//            });
//        }
//    }
//}