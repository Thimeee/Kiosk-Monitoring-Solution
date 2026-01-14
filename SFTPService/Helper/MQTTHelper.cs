using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.LowLevelClient;
using MQTTnet.Protocol;

namespace SFTPService.Helper
{
    public class MQTTHelper : IAsyncDisposable
    {
        private readonly LoggerService _log;
        private readonly IConfiguration _config;

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

        public MQTTHelper(LoggerService log, IConfiguration config)
        {
            _log = log;
            _config = config;
        }

        public async Task<bool> InitAsync(string host, string user, string pass, int port = 1883)
        {
            try
            {
                if (_disposed || _isShuttingDown)
                {
                    await SafeLog("MQTT Init", "Skipping init - service shutting down");
                    return false;
                }

                await CleanupAsync();

                var factory = new MqttClientFactory();
                _client = factory.CreateMqttClient();

                _options = new MqttClientOptionsBuilder()
                    .WithTcpServer(host, port)
                    .WithCredentials(user, pass)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .WithCleanSession(false) //  Persist sessions for reliability
                    .WithTimeout(TimeSpan.FromSeconds(ConnectionTimeoutSeconds))
                    .Build();

                // Attach event handlers ONCE
                _client.DisconnectedAsync += HandleDisconnectAsync;
                _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;

                //  Connect with timeout
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));
                await _client.ConnectAsync(_options, connectCts.Token);

                await SafeLog("MQTT", "✓ Connected to MQTT broker");

                //  Start health check timer
                StartHealthCheck();

                return true;
            }
            catch (OperationCanceledException)
            {
                await SafeLog("MQTT Init Error", "Connection timeout", 3);
                return false;
            }
            catch (Exception ex)
            {
                await SafeLog("MQTT Init Error", $"Failed: {ex}", 3);
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
                            await SafeLog("MQTT Health", "No activity for 5 minutes - verifying connection", 2);

                            // Try a ping by checking connection status
                            if (!_client.IsConnected)
                            {
                                await SafeLog("MQTT Health", "Connection lost - triggering reconnect", 2);
                                _ = Task.Run(async () => await TryReconnectAsync());
                            }
                        }
                    }
                    else if (!_disposed && !_isShuttingDown)
                    {
                        await SafeLog("MQTT Health", "Not connected - triggering reconnect", 2);
                        _ = Task.Run(async () => await TryReconnectAsync());
                    }
                }
                catch (Exception ex)
                {
                    await SafeLog("MQTT Health Error", $"{ex}", 3);
                }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)); // Check every minute
        }

        private async Task HandleDisconnectAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_disposed || _isShuttingDown) return;

            var reason = e.Reason.ToString();

            // Handle different disconnect scenarios
            if (e.Reason == MqttClientDisconnectReason.NormalDisconnection)
            {
                await SafeLog("MQTT", "Normal disconnection");
                return;
            }

            // Log disconnect reason with appropriate level
            var logLevel = e.ClientWasConnected ? 2 : 3; // Warning if was connected, Error if never connected
            await SafeLog("MQTT", $"Disconnected: {reason}", logLevel);

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

                for (int i = 1; i <= MaxReconnectAttempts; i++)
                {
                    // Exit if disposed or shutting down
                    if (_disposed || _isShuttingDown || token.IsCancellationRequested)
                    {
                        await SafeLog("MQTT", "Reconnect stopped - service shutting down");
                        break;
                    }

                    try
                    {
                        // Check if already connected
                        if (_client?.IsConnected == true)
                        {
                            await SafeLog("MQTT", "✓ Already reconnected");
                            await ResubscribeAllAsync();
                            if (OnReconnectedMQTT != null)
                                await OnReconnectedMQTT.Invoke();
                            consecutiveFailures = 0; // Reset failure count
                            return;
                        }

                        // Log reconnect attempt (less verbose after 10 attempts)
                        if (i <= 10 || i % 10 == 0)
                        {
                            await SafeLog("MQTT", $"Reconnect attempt {i}/{MaxReconnectAttempts}");
                        }

                        // Recreate client if necessary
                        if (_client == null)
                        {
                            var factory = new MqttClientFactory();
                            _client = factory.CreateMqttClient();
                            _client.DisconnectedAsync += HandleDisconnectAsync;
                            _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
                        }

                        // Try to connect with timeout
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));

                        await _client.ConnectAsync(_options!, connectCts.Token);

                        // SUCCESS!
                        await SafeLog("MQTT", $"✓ Reconnected successfully (attempt {i})");

                        // Resubscribe to all topics
                        await ResubscribeAllAsync();

                        // Notify listeners
                        if (OnReconnectedMQTT != null)
                            await OnReconnectedMQTT.Invoke();

                        consecutiveFailures = 0; // Reset failure count
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        await SafeLog("MQTT", "Reconnect canceled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        consecutiveFailures++;

                        // Only log errors periodically to avoid log spam
                        if (i <= 5 || i % 10 == 0)
                        {
                            await SafeLog("MQTT Reconnect", $"Attempt {i} failed: {ex.Message}", 2);
                        }

                        // On final attempt, log as error
                        if (i == MaxReconnectAttempts)
                        {
                            await SafeLog("MQTT Error",
                                $"Failed to reconnect after {MaxReconnectAttempts} attempts. Will keep trying...", 3);

                            //  Don't give up! Start over with attempt 1
                            i = 0; // Reset counter to keep trying forever
                            delay = InitialRetryDelayMs; // Reset delay
                        }
                    }

                    //  Exit check before delay
                    if (_disposed || _isShuttingDown || token.IsCancellationRequested)
                        break;

                    // Exponential backoff with jitter
                    try
                    {
                        int waitTime = delay + Random.Shared.Next(1000);
                        await Task.Delay(waitTime, token);

                        // Increase delay, but cap at MaxRetryDelayMs
                        delay = Math.Min(delay * 2, MaxRetryDelayMs);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                await SafeLog("MQTT", "Reconnect loop exited", 2);
            }
            catch (Exception ex)
            {
                await SafeLog("MQTT Reconnect Error", $"Unexpected error: {ex}", 3);
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private async Task ResubscribeAllAsync()
        {
            if (_subscriptions.Count == 0) return;

            await SafeLog("MQTT", $"Resubscribing to {_subscriptions.Count} topic(s)...");

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
                    await SafeLog("MQTT Resubscribe Error", $"Topic: {topic}, Error: {ex}", 3);
                }
            }

            await SafeLog("MQTT", $"✓ Resubscribed to {successCount}/{_subscriptions.Count} topic(s)");
        }

        public async Task PublishToServer(object payload, string topic, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            await PublishAsync(topic, payload, level, cancellationToken);
        }

        public async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
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
                    .WithRetainFlag(false)
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
                await SafeLog("MQTT Publish Error", $"Topic: {topic}, Error: {ex}", 3);

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
                await SafeLog("MQTT", $"✓ Subscribed to: {topic}");
            }
            catch (Exception ex)
            {
                await SafeLog("MQTT Subscribe Error", $"Topic: {topic}, Error: {ex}", 3);
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
                        await SafeLog("MQTT Handler Error", $"Topic: {topic}, Error: {ex}", 3);
                    }
                }
            }

            if (!handlerFound)
            {
                await SafeLog("MQTT", $"No handler found for topic: {topic}", 2);
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
            _isShuttingDown = true;

            await SafeLog("MQTT", "Graceful shutdown initiated");

            // Cancel reconnect attempts
            _reconnectCts?.Cancel();

            // Stop health check
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
                        await SafeLog("MQTT", "Disconnecting...");
                        await _client.DisconnectAsync();
                    }

                    _client.Dispose();
                    _client = null;
                }
            }
            catch (Exception ex)
            {
                await SafeLog("MQTT Cleanup Error", ex.Message, 2);
            }
        }

        // ✅ Safe logging helper
        private async Task SafeLog(string category, string message, int level = 1)
        {
            try
            {
                if (_log != null && !_disposed)
                    await _log.WriteLog(category, message, level);
            }
            catch
            {
                // Ignore logging errors
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