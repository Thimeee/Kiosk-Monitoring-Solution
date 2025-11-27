using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MonitoringBackend.Service;
using MQTTnet;
using MQTTnet.Protocol;

namespace MonitoringBackend.Helper
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
            if (!await _reconnectLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;

                const int maxRetry = 50;
                int delay = 1000;

                for (int i = 1; i <= maxRetry; i++)
                {
                    if (_disposed || token.IsCancellationRequested) break;

                    try
                    {
                        await _log.WriteLog("MQTT", $"Reconnect attempt {i}");

                        if (_client!.IsConnected)
                        {
                            await _log.WriteLog("MQTT", "Already reconnected");
                            return;
                        }

                        await _client.ConnectAsync(_options!, token);

                        await _log.WriteLog("MQTT", "Reconnected");

                        if (OnReconnectedMQTT != null) await OnReconnectedMQTT.Invoke();

                        return;
                    }
                    catch { }

                    await Task.Delay(delay + Random.Shared.Next(500), token);
                    delay = Math.Min(delay * 2, 30000);
                }
            }
            finally
            {
                _reconnectLock.Release();
            }
        }


        // ----------------------------------------------------------------------
        // PUBLISH METHODS
        // ----------------------------------------------------------------------

        public async Task PublishToServer(object payload, string topic, MqttQualityOfServiceLevel level)
        {
            await PublishAsync(topic, payload, level);
        }


        private async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel level)
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

                await _client.PublishAsync(msg);
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

            await _client.SubscribeAsync(topic); // "#" subscribes to all topics


            if (!_eventsBound)
            {
                // Subscribe to all topics using a wildcard if needed

                // Attach a single event handler
                _client.ApplicationMessageReceivedAsync += async e =>
                {
                    await _log.WriteLog("MQTT", "hi");

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

                await _log.WriteLog("MQTT", "Global handler set to receive all MQTT messages");
                _eventsBound = true; // stays TRUE forever while client exists
            }
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
