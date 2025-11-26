
//using System.Text;
//using System.Text.Json;
//using Monitoring.Shared.DTO;
//using RabbitMQ.Client;
//using RabbitMQ.Client.Events;

//namespace SFTPService.Helper
//{
//    public class RabbitHelper : IDisposable
//    {
//        private IConnection? _conn;
//        private IChannel? _ch;
//        private readonly LoggerService _log;
//        private readonly IConfiguration _config;
//        private bool _isReconnecting = false;


//        public const string EX_BROADCAST = "branch.broadcast.patch";
//        public const string EX_DIRECT = "branch.direct.patch";
//        public const string EX_STATUS = "branch.status.patch";
//        public const string EX_STATUS_ALL_PATCH = "all.status.patch";


//        private bool _disposed = false;
//        public event Func<Task> OnReconnected;
//        private CancellationTokenSource? _reconnectCts;
//        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
//        public RabbitHelper(LoggerService log, IConfiguration _config)
//        {
//            this._log = log;
//            this._config = _config;
//        }

//        public async Task<bool> RabbitHelperInit(string host, string user, string pass)
//        {

//            try
//            {
//                await CleanupConnectionAsync();

//                var factory = new ConnectionFactory
//                {
//                    HostName = host,
//                    UserName = user,
//                    Password = pass,
//                    AutomaticRecoveryEnabled = false, // We handle reconnection manually
//                    RequestedHeartbeat = TimeSpan.FromSeconds(60),
//                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
//                };

//                _conn = await factory.CreateConnectionAsync();
//                _ch = await _conn.CreateChannelAsync();

//                //Reconnect event
//                _conn.ConnectionShutdownAsync += OnConnectionShutdown;
//                // Declare exchanges
//                await _ch.ExchangeDeclareAsync(EX_BROADCAST, ExchangeType.Fanout, durable: true);
//                await _ch.ExchangeDeclareAsync(EX_DIRECT, ExchangeType.Direct, durable: true);
//                await _ch.ExchangeDeclareAsync(EX_STATUS, ExchangeType.Fanout, durable: true);
//                await _ch.ExchangeDeclareAsync(EX_STATUS_ALL_PATCH, ExchangeType.Fanout, durable: true);

//                return true;
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (RabbitHelperInit)", $"Failed to connect to RabbitMQ");
//                await _log.WriteLog("RabiMQ Exception (RabbitHelperInit)", $"Unexpected error: {ex}", 3);
//                return false;
//            }
//        }



//        private async Task OnConnectionShutdown(object? sender, ShutdownEventArgs args)
//        {
//            if (_disposed) return;

//            await _log.WriteLog("RabbitMQ", $"Connection shutdown detected: {args.ReplyText}");

//            // Don't reconnect if it's a normal shutdown
//            if (args.Initiator == ShutdownInitiator.Application)
//            {
//                await _log.WriteLog("RabbitMQ", "Application-initiated shutdown, not reconnecting");
//                return;
//            }

//            _ = Task.Run(async () => await ReconnectAsync());
//        }

//        private async Task ReconnectAsync()
//        {
//            if (_disposed) return;

//            // Use semaphore to prevent multiple concurrent reconnection attempts
//            if (!await _reconnectLock.WaitAsync(0))
//            {
//                await _log.WriteLog("RabbitMQ", "Reconnection already in progress");
//                return;
//            }

//            try
//            {
//                // Cancel any existing reconnection attempt
//                _reconnectCts?.Cancel();
//                _reconnectCts = new CancellationTokenSource();
//                var token = _reconnectCts.Token;

//                const int maxRetries = 100;
//                int retryDelayMs = 1000; // Start with 1 second

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {
//                    if (_disposed || token.IsCancellationRequested) break;

//                    try
//                    {
//                        // Check if connection is already healthy
//                        if (_conn?.IsOpen == true && _ch?.IsOpen == true)
//                        {
//                            await _log.WriteLog("RabbitMQ", "Connection already restored");
//                            break;
//                        }

//                        await _log.WriteLog("RabbitMQ", $"Reconnection attempt {attempt}/{maxRetries}");

//                        bool success = await RabbitHelperInit(
//                            _config["RabbitMQ:Host"] ?? "localhost",
//                            _config["RabbitMQ:Username"] ?? "guest",
//                            _config["RabbitMQ:Password"] ?? "guest"
//                        );

//                        if (success)
//                        {
//                            await _log.WriteLog("RabbitMQ", "Reconnection successful");

//                            // Notify subscribers to re-establish their queues
//                            if (OnReconnected != null)
//                            {
//                                await OnReconnected.Invoke();
//                            }

//                            break;
//                        }
//                        else
//                        {
//                            await _log.WriteLog("RabbitMQ", $"Reconnection attempt {attempt} failed");
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        await _log.WriteLog("RabbitMQ Exception (ReconnectAsync)",
//                            $"Attempt {attempt} failed: {ex.Message}", 3);
//                    }

//                    // Exponential backoff with jitter (max 30 seconds)
//                    int delay = Math.Min(retryDelayMs, 30000);
//                    int jitter = Random.Shared.Next(0, 1000);

//                    await _log.WriteLog("RabbitMQ", $"Waiting {delay}ms before retry {attempt + 1}");

//                    try
//                    {
//                        await Task.Delay(delay + jitter, token);
//                    }
//                    catch (TaskCanceledException)
//                    {
//                        break;
//                    }

//                    // Increase delay for next attempt (exponential backoff)
//                    retryDelayMs = Math.Min(retryDelayMs * 2, 30000);
//                }

//                if (_conn?.IsOpen != true || _ch?.IsOpen != true)
//                {
//                    await _log.WriteLog("RabbitMQ Error",
//                        "Failed to reconnect after maximum retries", 3);
//                }
//            }
//            finally
//            {
//                _reconnectLock.Release();
//            }
//        }

//        private async Task CleanupConnectionAsync()
//        {
//            try
//            {
//                if (_conn != null)
//                {
//                    _conn.ConnectionShutdownAsync -= OnConnectionShutdown;
//                }

//                if (_ch?.IsOpen == true)
//                {
//                    await _ch.CloseAsync();
//                }

//                if (_conn?.IsOpen == true)
//                {
//                    await _conn.CloseAsync();
//                }
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabbitMQ Warning",
//                    $"Error during cleanup: {ex.Message}", 2);
//            }
//        }

//        public async ValueTask DisposeAsync()
//        {
//            if (_disposed) return;
//            _disposed = true;

//            _reconnectCts?.Cancel();
//            await CleanupConnectionAsync();
//            _reconnectLock.Dispose();
//            _reconnectCts?.Dispose();
//        }

//        public void Dispose()
//        {
//            DisposeAsync().AsTask().GetAwaiter().GetResult();
//        }


//        public async Task PublishBroadcast(object payload)
//        {
//            try
//            {
//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabbitMQ Error (PublishBroadcast)", "Cannot subscribe - channel not available");
//                    return;
//                }
//                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
//                await _ch.BasicPublishAsync(EX_BROADCAST, "", body);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (PublishBroadcast)", $"Failed to PublishBroadcast event ");
//                await _log.WriteLog("RabiMQ Exception (PublishBroadcast)", $"Unexpected error: {ex}", 3);
//            }

//        }

//        public async Task PublishToBranch(string branchId, object payload)
//        {
//            try
//            {
//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabbitMQ Error (PublishToBranch)", "Cannot subscribe - channel not available");
//                    return;
//                }
//                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
//                await _ch.BasicPublishAsync(EX_DIRECT, branchId, body);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (PublishToBranch)", $"Failed to PublishToBranch event ");
//                await _log.WriteLog("RabiMQ Exception (PublishToBranch)", $"Unexpected error: {ex}", 3);
//            }
//        }

//        public async Task PublishStatusOnBranch(object payload)
//        {
//            try
//            {
//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabbitMQ Error (PublishStatusOnBranch)", "Cannot subscribe - channel not available");
//                    return;
//                }
//                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
//                await _ch.BasicPublishAsync(EX_STATUS, "", body);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (PublishStatus)", $"Failed to PublishStatus event ");
//                await _log.WriteLog("RabiMQ Exception (PublishStatus)", $"Unexpected error: {ex}", 3);
//            }
//        }

//        public async Task PublishAllBranchStatus(object payload)
//        {
//            try
//            {
//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabbitMQ Error (PublishAllBranchStatus)", "Cannot subscribe - channel not available");
//                    return;
//                }
//                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
//                await _ch.BasicPublishAsync(EX_STATUS_ALL_PATCH, "", body);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (PublishStatus)", $"Failed to PublishStatus event ");
//                await _log.WriteLog("RabiMQ Exception (PublishStatus)", $"Unexpected error: {ex}", 3);
//            }
//        }

//        public async Task SubscribeBranchQueue(string branchId, Func<string, Task> handler)
//        {
//            try
//            {
//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabiMQ Error (SubscribeBranchQueue)", $"RabbitMQ channel is not available");
//                    return;

//                }
//                string queueName = $"branch.queue.{branchId}.patch";

//                await _ch.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);

//                // Bind same queue to fanout and direct
//                await _ch.QueueBindAsync(queueName, EX_BROADCAST, "");
//                await _ch.QueueBindAsync(queueName, EX_DIRECT, branchId);

//                var consumer = new AsyncEventingBasicConsumer(_ch);
//                consumer.ReceivedAsync += async (ch, args) =>
//                {
//                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
//                    await handler(msg);
//                    await _ch.BasicAckAsync(args.DeliveryTag, false);
//                };

//                await _ch.BasicConsumeAsync(queueName, false, consumer);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (SubscribeBranchQueue)", $"Failed to SubscribeBranchQueue event ");
//                await _log.WriteLog("RabiMQ Exception (SubscribeBranchQueue)", $"Unexpected error: {ex}", 3);
//            }

//        }

//        public async Task SubscribeToStatusForAllPatch(Func<string, Task> handler)
//        {
//            try
//            {
//                string queue = "main.status.queue.all.patch";

//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabiMQ Error (SubscribeToStatusForAllPatch)", $"RabbitMQ channel is not available");
//                    return;

//                }
//                await _ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
//                await _ch.QueueBindAsync(queue, EX_STATUS_ALL_PATCH, "#"); // all routing keys (all branches)

//                var consumer = new AsyncEventingBasicConsumer(_ch);
//                consumer.ReceivedAsync += async (ch, args) =>
//                {
//                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
//                    await handler(msg);
//                    await _ch.BasicAckAsync(args.DeliveryTag, false);
//                };

//                await _ch.BasicConsumeAsync(queue, false, consumer);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (SubscribeToStatusForAllPatch)", $"Failed to SubscribeToStatusForAllPatch event ");
//                await _log.WriteLog("RabiMQ Exception (SubscribeToStatusForAllPatch)", $"Unexpected error: {ex}", 3);
//            }
//        }



//        public async Task SubscribeToStatusOneBranchPatch(Func<string, Task> handler)
//        {
//            try
//            {
//                string queue = "branch.status.queue.one.patch";

//                if (_ch == null || !_ch.IsOpen)
//                {
//                    await _log.WriteLog("RabiMQ Error (SubscribeToStatusOneBranchPatch)", $"RabbitMQ channel is not available");
//                    return;

//                }
//                await _ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
//                await _ch.QueueBindAsync(queue, EX_STATUS, "");

//                var consumer = new AsyncEventingBasicConsumer(_ch);
//                consumer.ReceivedAsync += async (ch, args) =>
//                {
//                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
//                    await handler(msg);
//                    await _ch.BasicAckAsync(args.DeliveryTag, false);
//                };

//                await _ch.BasicConsumeAsync(queue, false, consumer);
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("RabiMQ Error (SubscribeToStatusOneBranchPatch)", $"Failed to SubscribeToStatusOneBranchPatch event ");
//                await _log.WriteLog("RabiMQ Exception (SubscribeToStatusOneBranchPatch)", $"Unexpected error: {ex}", 3);
//            }
//        }




//        //SendOrPostCallback RabbitMQ 


//        public async Task SendStatusOnlyPatch(string branchId, string jobType, string jobMessage, bool status, int statusCode, FileDetails value, string command)
//        {
//            try
//            {

//                await _log.WriteLog("SendStatusBranch ", $"start RabbitMq Status Done UploadFileToMain");

//                var report = new BranchJobResponse<FileDetails>
//                {
//                    branchId = branchId,
//                    jobType = jobType,
//                    jobMessage = jobMessage,
//                    jobStatus = status,
//                    jobStatusCode = statusCode,
//                    jobRsValue = value,
//                    jobtime = DateTime.UtcNow,
//                    jobcommand = command
//                };

//                await PublishStatusOnBranch(report);
//                await _log.WriteLog("SendStatusBranch ", $"Send RabbitMq Status Done UploadFileToMain");

//                //_logger.LogInformation($"Status sent: {status}");

//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("Worker Error ", $" RabbitMq Status Done");
//                await _log.WriteLog("Worker Exception (SendStatusBranch)", $"Unexpected error: {ex}", 3);
//            }


//        }

//        public async Task SendStatusAllOnlyPatch(string branchId, string jobType, string jobMessage, bool status, int statusCode, FileDetails value, string command)
//        {
//            try
//            {

//                await _log.WriteLog("SendStatusAll ", $"start RabbitMq Status Done UploadFileToMain");

//                var report = new BranchJobResponse<FileDetails>
//                {
//                    branchId = branchId,
//                    jobType = jobType,
//                    jobMessage = jobMessage,
//                    jobStatus = status,
//                    jobStatusCode = statusCode,
//                    jobRsValue = value,
//                    jobtime = DateTime.UtcNow,
//                    jobcommand = command
//                };

//                await PublishAllBranchStatus(report);
//                await _log.WriteLog("SendStatusAll ", $"Send RabbitMq Status Done UploadFileToMain");

//                //_logger.LogInformation($"Status sent: {status}");
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("Worker Error ", $" RabbitMq Status Done");
//                await _log.WriteLog("Worker Exception (SendStatusAll)", $"Unexpected error: {ex}", 3);
//            }


//        }


//        public async Task SendStatusOnlySendFolderStucher(string branchId, string jobType, string jobMessage, bool status, int statusCode, FolderNode value, string command)
//        {
//            try
//            {

//                await _log.WriteLog("SendStatusOnlySendFolderStucher ", $"start RabbitMq Status Done UploadFileToMain");

//                var report = new BranchJobResponse<FolderNode>
//                {
//                    branchId = branchId,
//                    jobType = jobType,
//                    jobMessage = jobMessage,
//                    jobStatus = status,
//                    jobStatusCode = statusCode,
//                    jobRsValue = value,
//                    jobtime = DateTime.UtcNow,
//                    jobcommand = command
//                };

//                await PublishStatusOnBranch(report);
//                await _log.WriteLog("SendStatusOnlySendFolderStucher ", $"Send RabbitMq Status Done UploadFileToMain");

//                //_logger.LogInformation($"Status sent: {status}");
//            }
//            catch (Exception ex)
//            {
//                await _log.WriteLog("Worker Error ", $" RabbitMq Status Done");
//                await _log.WriteLog("Worker Exception (SendStatusBranch)", $"Unexpected error: {ex}", 3);
//            }


//        }
//    }
//}
