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

        private CancellationTokenSource? _reconnectCts;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _publishLock = new(50, 50); // Limit concurrent publishes

        private readonly ConcurrentDictionary<string, Func<string, string, Task>> _subscriptions = new();

        public event Func<Task>? OnReconnectedMQTT;

        private const int PublishTimeoutMs = 5000;
        private const int MaxReconnectAttempts = 50;
        private const int InitialRetryDelayMs = 1000;
        private const int MaxRetryDelayMs = 30000;

        public MQTTHelper(LoggerService log, IConfiguration config)
        {
            _log = log;
            _config = config;
        }

        public async Task<bool> InitAsync(string host, string user, string pass, int port = 1883)
        {
            try
            {
                await CleanupAsync();

                var factory = new MqttClientFactory();
                _client = factory.CreateMqttClient();

                _options = new MqttClientOptionsBuilder()
                    .WithTcpServer(host, port)
                    .WithCredentials(user, pass)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .WithCleanSession(false) // ✅ Persist sessions
                    .WithTimeout(TimeSpan.FromSeconds(10))
                    .Build();

                // Attach event handlers ONCE
                _client.DisconnectedAsync += HandleDisconnectAsync;
                _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;

                await _client.ConnectAsync(_options);
                await _log.WriteLog("MQTT", "Connected to MQTT broker");

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Init Error", $"Failed: {ex.Message}", 3);
                return false;
            }
        }

        private async Task HandleDisconnectAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_disposed) return;

            await _log.WriteLog("MQTT", $"Disconnected: {e.Reason}");

            if (e.ClientWasConnected)
            {
                _ = Task.Run(async () => await TryReconnectAsync());
            }
        }

        private async Task TryReconnectAsync()
        {
            // Non-blocking check
            if (!await _reconnectLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;

                int delay = InitialRetryDelayMs;

                for (int i = 1; i <= MaxReconnectAttempts; i++)
                {
                    if (_disposed || token.IsCancellationRequested)
                        break;

                    try
                    {
                        if (_client?.IsConnected == true)
                        {
                            await _log.WriteLog("MQTT", "Already reconnected");
                            await ResubscribeAllAsync();
                            if (OnReconnectedMQTT != null)
                                await OnReconnectedMQTT.Invoke();
                            return;
                        }

                        await _log.WriteLog("MQTT", $"Reconnect attempt {i}/{MaxReconnectAttempts}");

                        await _client!.ConnectAsync(_options!, token);
                        await _log.WriteLog("MQTT", "Reconnected successfully");

                        await ResubscribeAllAsync();
                        if (OnReconnectedMQTT != null)
                            await OnReconnectedMQTT.Invoke();
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (i == MaxReconnectAttempts)
                        {
                            await _log.WriteLog("MQTT Error", $"Reconnect failed after {i} attempts: {ex.Message}", 3);
                        }
                    }

                    await Task.Delay(delay + Random.Shared.Next(500), token);
                    delay = Math.Min(delay * 2, MaxRetryDelayMs);
                }
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private async Task ResubscribeAllAsync()
        {
            foreach (var topic in _subscriptions.Keys)
            {
                try
                {
                    await _client!.SubscribeAsync(topic);
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("MQTT Resubscribe Error", $"Topic: {topic}, Error: {ex.Message}", 3);
                }
            }
        }

        public async Task PublishToServer(object payload, string topic, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            await PublishAsync(topic, payload, level, cancellationToken);
        }

        public async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            if (_client == null || !_client.IsConnected)
            {
                return; // Silent fail
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
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation - don't log every one
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Publish Error", $"Topic: {topic}, Error: {ex.Message}", 3);
            }
            finally
            {
                _publishLock.Release();
            }
        }

        public async Task SubscribeAsync(string topic, Func<string, string, Task> handler)
        {
            if (_client == null) return;

            // Store handler for resubscription
            _subscriptions[topic] = handler;

            try
            {
                await _client.SubscribeAsync(topic);
                await _log.WriteLog("MQTT", $"Subscribed to: {topic}");
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Subscribe Error", $"Topic: {topic}, Error: {ex.Message}", 3);
            }
        }

        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string topic = e.ApplicationMessage.Topic;
            string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            foreach (var sub in _subscriptions)
            {
                if (TopicMatches(sub.Key, topic))
                {
                    try
                    {
                        await sub.Value(payload, topic);
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteLog("MQTT Handler Error", $"Topic: {topic}, Error: {ex.Message}", 3);
                    }
                }
            }
        }

        private bool TopicMatches(string pattern, string topic)
        {
            if (pattern == topic) return true;
            if (pattern.EndsWith("#"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return topic.StartsWith(prefix);
            }
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

        private async Task CleanupAsync()
        {
            try
            {
                if (_client != null)
                {
                    _client.DisconnectedAsync -= HandleDisconnectAsync;
                    _client.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;

                    if (_client.IsConnected)
                    {
                        await _client.DisconnectAsync();
                    }

                    _client.Dispose();
                    _client = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();

            await CleanupAsync();

            _reconnectLock?.Dispose();
            _publishLock?.Dispose();
        }
    }
}