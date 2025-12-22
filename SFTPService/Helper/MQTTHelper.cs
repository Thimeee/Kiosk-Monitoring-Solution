using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;

namespace SFTPService.Helper
{
    public class MQTTHelper : IDisposable
    {
        private readonly LoggerService _log;
        private readonly IConfiguration _config;

        private IMqttClient? _client;
        private MqttClientOptions? _options;
        private bool _disposed;

        private CancellationTokenSource? _reconnectCts;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);

        // MQTT “exchanges” → using topic patterns
        public const string TOPIC_BROADCAST = "branch/all/patch";
        public const string TOPIC_DIRECT = "branch/{0}/patch";
        public const string TOPIC_STATUS = "branch/status/patch";
        public const string TOPIC_STATUS_ALL = "branch/status/allpatch";

        public event Func<Task>? OnReconnectedMQTT;

        public MQTTHelper(LoggerService log, IConfiguration config)
        {
            _log = log;
            _config = config;
        }

        // ----------------------------------------------------------------------
        // INIT CONNECTION
        // ----------------------------------------------------------------------

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
                    .WithCleanSession(true)
                    .Build();

                _client.DisconnectedAsync += HandleDisconnect;

                await _client.ConnectAsync(_options);

                await _log.WriteLog("MQTT", "Connected to MQTT broker");

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Init Error", ex.ToString(), 3);
                return false;
            }
        }

        // ----------------------------------------------------------------------
        // AUTO RECONNECT
        // ----------------------------------------------------------------------

        private async Task HandleDisconnect(MqttClientDisconnectedEventArgs e)
        {
            if (_disposed) return;

            await _log.WriteLog("MQTT", $"Disconnected: {e.ReasonString}");

            if (e.ClientWasConnected)
                _ = Task.Run(async () => await TryReconnectAsync());
        }

        private async Task TryReconnectAsync()
        {
            await _reconnectLock.WaitAsync();
            try
            {
                // Cancel any previous reconnect attempts
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;

                const int maxRetry = 50;
                int delay = 1000; // initial delay in ms

                for (int i = 1; i <= maxRetry; i++)
                {
                    if (_disposed || token.IsCancellationRequested)
                    {
                        await SafeLog("MQTT", "Reconnect canceled or disposed");
                        break;
                    }

                    try
                    {
                        await SafeLog("MQTT", $"Reconnect attempt {i}");

                        if (_client != null && _client.IsConnected)
                        {
                            await SafeLog("MQTT", "Already connected");
                            if (OnReconnectedMQTT != null) await OnReconnectedMQTT.Invoke();
                            return;
                        }

                        if (_client != null && _options != null)
                        {
                            await _client.ConnectAsync(_options, token);
                            await SafeLog("MQTT", "Reconnected successfully");

                            if (OnReconnectedMQTT != null) await OnReconnectedMQTT.Invoke();
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await SafeLog("MQTT", "Reconnect attempt canceled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        await SafeLog("MQTT Error", $"Reconnect attempt {i} failed: {ex.Message}", 3);
                    }

                    // Exponential backoff with jitter
                    int waitTime = delay + Random.Shared.Next(500);
                    await Task.Delay(waitTime, token);
                    delay = Math.Min(delay * 2, 30000); // cap at 30s
                }

                await SafeLog("MQTT", "Max reconnect attempts reached");
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        // Helper to ensure logging never throws
        private async Task SafeLog(string category, string message, int level = 1)
        {
            try
            {
                if (_log != null)
                    await _log.WriteLog(category, message, level);
            }
            catch
            {

            }
        }


        // ----------------------------------------------------------------------
        // PUBLISH METHODS
        // ----------------------------------------------------------------------

        public async Task PublishToServer(object payload, string topic, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            await PublishAsync(topic, payload, level, cancellationToken);
        }


        public async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel level, CancellationToken cancellationToken)
        {
            try
            {
                if (_client == null || !_client.IsConnected)
                {
                    await _log.WriteLog("MQTT Publish", "Client not connected");
                    return;
                }
                var body = JsonSerializer.SerializeToUtf8Bytes(payload);

                var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(body)
                    .WithQualityOfServiceLevel(level)
                    .Build();

                await _client.PublishAsync(msg, cancellationToken);
            }
            catch (Exception ex)
            {
                await _log.WriteLog("MQTT Publish Error", ex.ToString(), 3);
            }
        }



        // ----------------------------------------------------------------------
        // SUBSCRIBE
        // ----------------------------------------------------------------------

        private bool _eventsBound = false;
        public async Task SubscribeAsync(string topic, Func<string, string, Task> handler)
        {
            if (_client == null) return;

            // Subscribe to all topics using a wildcard if needed
            await _client.SubscribeAsync(topic); // "#" subscribes to all topics

            if (!_eventsBound)
            {
                // Attach a single event handler
                _client.ApplicationMessageReceivedAsync += async e =>
            {
                //await _log.WriteLog("MQTT", "hi");

                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                try
                {
                    if (handler != null)
                    {
                        await handler(payload, topic); // pass both payload and topic
                    }
                }
                catch (Exception ex)
                {
                    await _log.WriteLog("MQTT Error", $"Global handler exception for topic {topic}: {ex}", 3);
                }
            };

            }
            _eventsBound = true; // stays TRUE forever while client exists
        }

        // ----------------------------------------------------------------------
        public async Task CleanupAsync()
        {
            try
            {
                if (_client != null)
                {
                    await _client.DisconnectAsync();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            _reconnectCts?.Cancel();
            await CleanupAsync();
        }
    }
}
