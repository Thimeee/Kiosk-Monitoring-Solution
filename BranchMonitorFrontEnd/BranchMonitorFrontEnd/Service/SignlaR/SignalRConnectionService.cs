//using BranchMonitorFrontEnd.Helper;
//using Microsoft.AspNetCore.SignalR.Client;

//namespace BranchMonitorFrontEnd.Service.SignlaR
//{
//    public class SignalRConnectionService : IAsyncDisposable
//    {
//        private HubConnection? _hubConnection;
//        private readonly string _hubUrl;

//        // Events for state changes
//        public event Action<bool>? ConnectionStateChanged;
//        public event Action<string>? MessageReceived;

//        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
//        public string? ConnectionId => _hubConnection?.ConnectionId;
//        public HubConnectionState? State => _hubConnection?.State;

//        public SignalRConnectionService(AppSettings settings)
//        {
//            // Get hub URL from appsettings.json
//            _hubUrl = settings.SignlRHubUrl;
//        }

//        // ✅ Manual START - Only starts when you call it
//        public async Task<bool> StartConnectionAsync()
//        {
//            try
//            {
//                if (_hubConnection != null)
//                {
//                    // Already have a connection
//                    if (_hubConnection.State == HubConnectionState.Connected)
//                    {
//                        Console.WriteLine("✅ Already connected");
//                        return true;
//                    }

//                    // Clean up old connection
//                    await DisposeAsync();
//                }

//                // Build new connection
//                _hubConnection = new HubConnectionBuilder()
//                    .WithUrl(_hubUrl)
//                    .WithAutomaticReconnect()
//                    .Build();

//                // Register message handler
//                _hubConnection.On<string>("BranchUpdate", (data) =>
//                {
//                    Console.WriteLine($"📩 Received: {data}");
//                    MessageReceived?.Invoke(data);
//                });

//                // Connection state handlers
//                _hubConnection.Reconnecting += error =>
//                {
//                    Console.WriteLine("🔄 Reconnecting...");
//                    ConnectionStateChanged?.Invoke(false);
//                    return Task.CompletedTask;
//                };

//                _hubConnection.Reconnected += connectionId =>
//                {
//                    Console.WriteLine($"✅ Reconnected: {connectionId}");
//                    ConnectionStateChanged?.Invoke(true);
//                    return Task.CompletedTask;
//                };

//                _hubConnection.Closed += async error =>
//                {
//                    Console.WriteLine($"❌ Connection closed: {error?.Message}");
//                    ConnectionStateChanged?.Invoke(false);
//                };

//                // Start connection
//                await _hubConnection.StartAsync();
//                Console.WriteLine($"✅ SignalR connected - ConnectionId: {_hubConnection.ConnectionId}");
//                ConnectionStateChanged?.Invoke(true);

//                return true;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"❌ Failed to start connection: {ex.Message}");
//                ConnectionStateChanged?.Invoke(false);
//                return false;
//            }
//        }

//        // ✅ Manual STOP
//        public async Task StopConnectionAsync()
//        {
//            try
//            {
//                if (_hubConnection != null)
//                {
//                    await _hubConnection.StopAsync();
//                    Console.WriteLine("🛑 SignalR connection stopped");
//                    ConnectionStateChanged?.Invoke(false);
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"❌ Error stopping connection: {ex.Message}");
//            }
//        }

//        // Get connection status
//        public bool GetConnectionStatus()
//        {
//            return IsConnected;
//        }

//        // Send message to server (optional)
//        public async Task SendMessageAsync(string methodName, object data)
//        {
//            if (_hubConnection?.State == HubConnectionState.Connected)
//            {
//                await _hubConnection.InvokeAsync(methodName, data);
//            }
//            else
//            {
//                throw new InvalidOperationException("Not connected to SignalR hub");
//            }
//        }

//        public async ValueTask DisposeAsync()
//        {
//            if (_hubConnection != null)
//            {
//                try
//                {
//                    await _hubConnection.DisposeAsync();
//                    Console.WriteLine("🧹 SignalR connection disposed");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error disposing connection: {ex.Message}");
//                }
//                finally
//                {
//                    _hubConnection = null;
//                }
//            }
//        }
//    }
//}
