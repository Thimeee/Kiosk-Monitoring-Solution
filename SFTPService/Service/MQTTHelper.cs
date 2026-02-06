using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;
using MQTTnet;
using MQTTnet.LowLevelClient;
using MQTTnet.Protocol;

namespace SFTPService.Service
{
    public class MQTTHelper : IAsyncDisposable
    {
        private readonly LoggerService _log;
        private readonly AppConfig _config;

        private IMqttClient? _client;
        private MqttClientOptions? _options;
        private bool _disposed;
        private bool _isShuttingDown; // NEW: Track shutdown state

        private CancellationTokenSource? _reconnectCts;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _publishLock = new(50, 50);

        private readonly ConcurrentDictionary<string, Func<string, string, Task>> _subscriptions = new();

        public event Func<Task>? OnReconnectedMQTT;

        private const int PublishTimeoutMs = 5000;
        private const int MaxReconnectAttempts = 100; //  Increased for broker downtime
        private const int InitialRetryDelayMs = 1000;
        private const int MaxRetryDelayMs = 60000; //  Increased to 60 seconds
        private const int ConnectionTimeoutSeconds = 10;

        // NEW: Health monitoring
        private Timer? _healthCheckTimer;
        private DateTime _lastSuccessfulMessage = DateTime.Now;

        public MQTTHelper(LoggerService log, IOptions<AppConfig> config)
        {
            _log = log;
            _config = config.Value;
        }

        public async Task<bool> InitAsync(string host, string user, string pass, int port = 1883)
        {
            try
            {
                var branchId = _config.BranchId;

                if (_disposed || _isShuttingDown)
                {
                    await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Init", "Skipping init - service shutting down");

                    return false;
                }

                await CleanupAsync();

                var factory = new MqttClientFactory();
                _client = factory.CreateMqttClient();

                _options = new MqttClientOptionsBuilder()
                    .WithClientId(branchId)
                    .WithTcpServer(host, port)
                    .WithCredentials(user, pass)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(10))
                    .WithCleanSession(false) //  Persist sessions for reliability
                    .WithWillTopic($"server/{branchId}/STATUS/MQTTStatus")
.WithWillPayload(((int)MQTTConnectionStatus.OFFLINE).ToString())
.WithWillRetain(true)
.WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTimeout(TimeSpan.FromSeconds(ConnectionTimeoutSeconds))
                    .Build();

                // Attach event handlers ONCE
                _client.DisconnectedAsync += HandleDisconnectAsync;
                _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;

                //  Connect with timeout
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));
                await _client.ConnectAsync(_options, connectCts.Token);

                await _log.WriteLogAsync(LogType.Initial, "SUCCES:MQTT-Init", "Service Initial Connected to MQTT broker Oky");


                //  Start health check timer
                StartHealthCheck();

                return true;
            }
            catch (OperationCanceledException)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:MQTT-Init-Error", "Connection timeout Check appliction.json MQTT credential Correct or restart Service ");

                return false;
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Init-Error", $"Failed: {ex}");

                return false;
            }
        }

        //  NEW: Periodic health check
        private void StartHealthCheck()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = new Timer(async _ =>
            {
                if (_disposed || _isShuttingDown) return;

                try
                {
                    // Check if connection is healthy
                    if (_client != null && _client.IsConnected)
                    {
                        // Check if we've received messages recently (last 5 minutes)
                        var timeSinceLastMessage = DateTime.Now - _lastSuccessfulMessage;
                        if (timeSinceLastMessage.TotalMinutes > 5)
                        {

                            // Try a ping by checking connection status
                            if (!_client.IsConnected)
                            {
                                await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Health-WRN", $"Connection lost - triggering reconnect");
                                _ = Task.Run(async () => await TryReconnectAsync());
                            }
                        }
                    }
                    else if (!_disposed && !_isShuttingDown)
                    {
                        await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Health-WRN", $"Connection lost - triggering reconnect");
                        _ = Task.Run(async () => await TryReconnectAsync());
                    }
                }
                catch (Exception ex)
                {
                    await _log.WriteLogAsync(LogType.Exception, "WRN:MQTT-Health-ERROR", $"{ex}");

                }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)); // Check every minute
        }

        private async Task HandleDisconnectAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_disposed || _isShuttingDown) return;

            var reason = e.Reason.ToString();

            if (e.ClientWasConnected && !_client.IsConnected)
            {
                await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Disconn", $"Network lost or broker unreachable, triggering reconnect");

                _ = Task.Run(async () => await TryReconnectAsync());
                return;
            }
            // Handle different disconnect scenarios
            if (e.Reason == MqttClientDisconnectReason.NormalDisconnection)
            {
                await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Disconn", $"Normal disconnection");

                return;
            }

            // Log disconnect reason with appropriate level
            await _log.WriteLogAsync(LogType.Delay, "WRN:MQTT-Disconn", $"Disconnected: {reason}");


            // Always try to reconnect (unless shutting down)
            if (!_disposed && !_isShuttingDown)
            {
                _ = Task.Run(async () => await TryReconnectAsync());
            }
        }

        private async Task TryReconnectAsync()
        {
            // Non-blocking check - exit if already reconnecting
            if (!await _reconnectLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                // Cancel previous reconnect attempts
                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;

                int delay = InitialRetryDelayMs;
                int consecutiveFailures = 0;

                int attempt = 0;

                while (!_disposed && !_isShuttingDown && !token.IsCancellationRequested)
                {
                    attempt++;

                    try
                    {
                        if (_client?.IsConnected == true)
                        {
                            await ResubscribeAllAsync();
                            await OnReconnectedMQTT?.Invoke();
                            return;
                        }

                        if (attempt <= 10 || attempt % 10 == 0)
                        {
                            await _log.WriteLogAsync(
                                LogType.Connection,
                                "INFO:MQTT-Reconnect",
                                $"Reconnect attempt {attempt}"
                            );
                        }

                        if (_client == null)
                        {
                            var factory = new MqttClientFactory();
                            _client = factory.CreateMqttClient();
                            _client.DisconnectedAsync += HandleDisconnectAsync;
                            _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
                        }

                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));

                        await _client.ConnectAsync(_options!, connectCts.Token);

                        await _log.WriteLogAsync(
                            LogType.Connection,
                            "SUCCESS:MQTT-Reconnect",
                            $"Reconnected successfully (attempt {attempt})"
                        );

                        await ResubscribeAllAsync();
                        await OnReconnectedMQTT?.Invoke();

                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt <= 5 || attempt % 10 == 0)
                        {
                            await _log.WriteLogAsync(
                                LogType.Exception,
                                "ERROR:MQTT-Reconnect",
                                $"Attempt {attempt} failed: {ex.Message}"
                            );
                        }
                    }

                    int waitTime = delay + Random.Shared.Next(1000);
                    await Task.Delay(waitTime, token);
                    delay = Math.Min(delay * 2, MaxRetryDelayMs);
                }

            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Reconnect", $"Unexpected error: {ex}");

            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private async Task ResubscribeAllAsync()
        {
            if (_subscriptions.Count == 0) return;



            int successCount = 0;
            foreach (var topic in _subscriptions.Keys)
            {
                try
                {
                    await _client!.SubscribeAsync(topic);
                    successCount++;
                }
                catch (Exception ex)
                {
                    await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Resubscribe", $"Topic: {topic}, Error: {ex}");

                }
            }

            await _log.WriteLogAsync(LogType.Delay, "SUCCES:MQTT-Resubscribe", $"Resubscribed to {successCount}/{_subscriptions.Count} topic(s)");

        }

        public async Task PublishToServer(object payload, string topic, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            await PublishAsync(topic, payload, level, cancellationToken);
        }

        public async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel level, CancellationToken cancellationToken, bool withRetainFlag = false)
        {
            //  Check if connected
            if (_client == null || !_client.IsConnected)
            {
                // Trigger reconnect if not already trying
                if (!_disposed && !_isShuttingDown)
                {
                    _ = Task.Run(async () => await TryReconnectAsync());
                }
                return; // Silent fail - message will be lost
            }

            await _publishLock.WaitAsync(cancellationToken);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(PublishTimeoutMs);

                var body = JsonSerializer.SerializeToUtf8Bytes(payload);

                var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(body)
                    .WithQualityOfServiceLevel(level)
                    .WithRetainFlag(withRetainFlag)
                    .Build();

                await _client.PublishAsync(msg, cts.Token);

                // Update last successful activity
                _lastSuccessfulMessage = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                // Timeout - don't spam logs
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Publish", $"Topic: {topic}, Error: {ex}");


                // Connection might be dead - trigger reconnect
                if (!_disposed && !_isShuttingDown)
                {
                    _ = Task.Run(async () => await TryReconnectAsync());
                }
            }
            finally
            {
                _publishLock.Release();
            }
        }

        public async Task SubscribeAsync(string topic, Func<string, string, Task> handler)
        {
            if (_client == null) return;

            // Store handler for resubscription after disconnect
            _subscriptions[topic] = handler;

            try
            {
                await _client.SubscribeAsync(topic);
                await _log.WriteLogAsync(LogType.Delay, "SUCCES:MQTT-Subscribe", $"Subscribed to: {topic}");

            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Subscribe", $"Topic: {topic}, Error: {ex}");

            }
        }

        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            // Update last activity timestamp
            _lastSuccessfulMessage = DateTime.Now;

            string topic = e.ApplicationMessage.Topic;
            string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            // Find matching handlers
            bool handlerFound = false;
            foreach (var sub in _subscriptions)
            {
                if (TopicMatches(sub.Key, topic))
                {
                    handlerFound = true;
                    try
                    {
                        await sub.Value(payload, topic);
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLogAsync(LogType.Exception, "ERROR:MQTT-Handler", $"Topic: {topic}, Error: {ex}");

                    }
                }
            }

            if (!handlerFound)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:MQTT-Handler", $"No handler found for topic: {topic}");

            }
        }

        private bool TopicMatches(string pattern, string topic)
        {
            if (pattern == topic) return true;

            // Multi-level wildcard (#)
            if (pattern.EndsWith("#"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return topic.StartsWith(prefix);
            }

            // Single-level wildcard (+)
            if (pattern.Contains("+"))
            {
                var parts = pattern.Split('/');
                var topicParts = topic.Split('/');
                if (parts.Length != topicParts.Length) return false;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] != "+" && parts[i] != topicParts[i])
                        return false;
                }
                return true;
            }

            return false;
        }

        // NEW: Public method to gracefully shutdown
        public async Task ShutdownAsync()
        {
            string branchId = _config.BranchId;
            _isShuttingDown = true;

            await _log.WriteLogAsync(LogType.Delay, "SUCCES:MQTT-Shutdown", "Graceful shutdown initiated");


            if (_client != null && _client.IsConnected)
            {

                //Send Worker Service Status
                await PublishAsync(
        $"server/{branchId}/STATUS/ServiceStatus",
        ServiceConnectionStatus.OFFLINE,
        MqttQualityOfServiceLevel.AtLeastOnce,
         CancellationToken.None,
         true
    );

            }

            // Stop reconnects and health checks
            _reconnectCts?.Cancel();
            _healthCheckTimer?.Dispose();

            await CleanupAsync();
        }


        public async Task CleanupAsync()
        {
            try
            {
                if (_client != null)
                {
                    // Unsubscribe from events
                    _client.DisconnectedAsync -= HandleDisconnectAsync;
                    _client.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;

                    // Disconnect gracefully
                    if (_client.IsConnected)
                    {
                        await _log.WriteLogAsync(LogType.Delay, "SUCCES:MQTT-Cleanup", "Disconnecting...");

                        await _client.DisconnectAsync();
                    }

                    _client.Dispose();
                    _client = null;
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:MQTT-Cleanup", $"MQTT Cleanup Error");

            }
        }



        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _isShuttingDown = true;

            // Cancel reconnection
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();

            // Stop health check
            _healthCheckTimer?.Dispose();

            // Cleanup MQTT
            await CleanupAsync();

            // Dispose locks
            _reconnectLock?.Dispose();
            _publishLock?.Dispose();
        }
    }
}