using System.Net.Sockets;
using System.Net.WebSockets;
using TestAPINoVNC;

namespace NoVncBlazor.Server;

public class VncWebSocketProxy
{
    private const int BufferSize = 131072; // INCREASED to 128KB for larger frame updates
    private VncProxyFileLogger _logger;
    public VncWebSocketProxy()
    {
        _logger = new VncProxyFileLogger();
    }

    public async Task HandleWebSocketAsync(WebSocket webSocket, string vncHost, int vncPort)
    {
        TcpClient? tcpClient = null;
        NetworkStream? networkStream = null;
        try
        {
            tcpClient = new TcpClient();

            // CRITICAL: Optimize TCP for VNC
            tcpClient.NoDelay = true;                    // Disable Nagle's algorithm
            tcpClient.SendBufferSize = BufferSize * 2;   // Larger send buffer
            tcpClient.ReceiveBufferSize = BufferSize * 2; // Larger receive buffer

            // Enable TCP KeepAlive
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await tcpClient.ConnectAsync(vncHost, vncPort, connectCts.Token);
            }

            if (!tcpClient.Connected)
            {
                throw new Exception("Failed to connect to VNC server");
            }

            //VncProxyFileLogger.Log("TCP connection established");

            networkStream = tcpClient.GetStream();
            networkStream.ReadTimeout = Timeout.Infinite;
            networkStream.WriteTimeout = Timeout.Infinite;

            //_logger.LogInformation("Successfully connected to VNC server");
            //VncProxyFileLogger.Log("NetworkStream created, starting bidirectional forwarding");

            using var sessionCts = new CancellationTokenSource();

            var wsToVnc = ForwardWebSocketToVnc(webSocket, networkStream, sessionCts.Token);
            var vncToWs = ForwardVncToWebSocket(networkStream, webSocket, sessionCts.Token);

            var completedTask = await Task.WhenAny(wsToVnc, vncToWs);
            //VncProxyFileLogger.Log($"One direction completed: {(completedTask == wsToVnc ? "WebSocket->VNC" : "VNC->WebSocket")}");

            sessionCts.Cancel();

            await Task.WhenAll(
                Task.Run(async () => { try { await wsToVnc; } catch { } }),
                Task.Run(async () => { try { await vncToWs; } catch { } })
            );


        }
        catch (OperationCanceledException)
        {
            await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync)", $"Connection timeout or cancelled");
            await _logger.WriteLog("Exception- HandleWebSocketAsync(OperationCanceledException)", $"Connection timeout or cancelled", 3);


        }
        catch (SocketException ex)
        {
            await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync )", $"Socket error connecting to VNC server: {ex.Message}");
            await _logger.WriteLog("Exception- HandleWebSocketAsync(SocketException)", $"{ex.ToString()}", 3);

        }
        catch (Exception ex)
        {
            await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync )", $"Error in WebSocket proxy: {ex.Message}");
            await _logger.WriteLog("Exception- HandleWebSocketAsync(Exception)", $"{ex.ToString()}", 3);

        }
        finally
        {
            //VncProxyFileLogger.Log("Cleanup starting...");

            try
            {
                networkStream?.Close();
                networkStream?.Dispose();
                //VncProxyFileLogger.Log("NetworkStream closed");
            }
            catch (Exception ex)
            {
                await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync )", $"Error closing NetworkStream: {ex.Message}");
                await _logger.WriteLog("Exception- HandleWebSocketAsync(Exception)", $"{ex.ToString()}", 3);
            }

            try
            {
                tcpClient?.Close();
                tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync )", $"Error closing TcpClient: {ex.Message}");
                await _logger.WriteLog("Exception- HandleWebSocketAsync(Exception)", $"{ex.ToString()}", 3);
            }

            try
            {
                if (webSocket.State == WebSocketState.Open ||
                    webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        CancellationToken.None);
                }
                else
                {
                    //VncProxyFileLogger.Log($"WebSocket already closed. State: {webSocket.State}");
                }
            }
            catch (Exception ex)
            {

                await _logger.WriteLog("ERROR:(EX- HandleWebSocketAsync )", $"Error closing WebSocket: {ex.Message}");
                await _logger.WriteLog("Exception- HandleWebSocketAsync(Exception)", $"{ex.ToString()}", 3);
            }

        }
    }

    private async Task ForwardWebSocketToVnc(WebSocket webSocket, NetworkStream networkStream, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await _logger.WriteLog("ERROR:(EX- ForwardWebSocketToVnc )", $"WebSocket receive cancelled");
                    await _logger.WriteLog("Exception- ForwardWebSocketToVnc(OperationCanceledException)", $"WebSocket receive cancelled", 3);
                    break;
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    await _logger.WriteLog("ERROR:(EX- ForwardWebSocketToVnc )", $"WebSocket closed prematurely by client");
                    await _logger.WriteLog("Exception- ForwardWebSocketToVnc(WebSocketException)", $"{ex.ToString()}", 3);

                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {

                    //VncProxyFileLogger.Log("WebSocket close message received from client");
                    break;
                }

                if (result.Count > 0)
                {
                    try
                    {
                        // Write data immediately without buffering
                        await networkStream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                    }
                    catch (IOException ex)
                    {
                        await _logger.WriteLog("ERROR:(EX- ForwardWebSocketToVnc )", $"Error writing to VNC server: {ex.Message}");
                        await _logger.WriteLog("Exception- ForwardWebSocketToVnc(WebSocketException)", $"{ex.ToString()}", 3);

                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.WriteLog("ERROR:(EX- ForwardWebSocketToVnc )", $"Error forwarding WebSocket to VNC: {ex.Message}");
            await _logger.WriteLog("Exception- ForwardWebSocketToVnc(OperationCanceledException)", $"{ex.ToString()}", 3);
        }
        finally
        {
        }
    }

    private async Task ForwardVncToWebSocket(NetworkStream networkStream, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        int totalBytesForwarded = 0;
        int frameCount = 0;

        try
        {
            while (webSocket.State == WebSocketState.Open &&
                   networkStream.CanRead &&
                   !cancellationToken.IsCancellationRequested)
            {
                int bytesRead;

                try
                {
                    // Read larger chunks for full frame updates
                    bytesRead = await networkStream.ReadAsync(buffer.AsMemory(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    //VncProxyFileLogger.Log("VNC read cancelled");
                    await _logger.WriteLog("ERROR:(EX- ForwardVncToWebSocket )", $"VNC read cancelled");
                    await _logger.WriteLog("Exception- ForwardVncToWebSocket(OperationCanceledException)", $"VNC read cancelled", 3);
                    break;
                }
                catch (IOException ex)
                {
                    await _logger.WriteLog("ERROR:(EX- ForwardVncToWebSocket )", $"Error reading from VNC server: {ex.Message}");
                    await _logger.WriteLog("Exception- ForwardVncToWebSocket(IOException)", $"{ex.ToString()}", 3);
                    //_logger.LogError(ex, "Error reading from VNC server");
                    break;
                }

                if (bytesRead == 0)
                {
                    //VncProxyFileLogger.Log("VNC server closed the connection (0 bytes read)");
                    //VncProxyFileLogger.Log($"Total bytes forwarded from VNC: {totalBytesForwarded}");
                    break;
                }

                totalBytesForwarded += bytesRead;
                frameCount++;

                // Log every 100 frames instead of by bytes
                if (frameCount % 100 == 0)
                {
                    //_logger.LogDebug($"Forwarded {frameCount} frames, {totalBytesForwarded / 1048576}MB total");
                }

                try
                {
                    // Send full buffer immediately
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(buffer, 0, bytesRead),
                        WebSocketMessageType.Binary,
                        true,
                        cancellationToken);
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    await _logger.WriteLog("ERROR:(EX- ForwardVncToWebSocket )", $"WebSocket closed prematurely while sending to client: {ex.Message}");
                    await _logger.WriteLog("Exception- ForwardVncToWebSocket(WebSocketException)", $"{ex.ToString()}", 3);
                    break;
                }
                catch (OperationCanceledException)
                {
                    await _logger.WriteLog("ERROR:(EX- ForwardVncToWebSocket )", $"WebSocket send cancelled");
                    await _logger.WriteLog("Exception- ForwardVncToWebSocket(OperationCanceledException)", $"WebSocket send cancelled", 3);
                    //VncProxyFileLogger.Log("WebSocket send cancelled");
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            //VncProxyFileLogger.Log($"Error forwarding VNC to WebSocket: {ex.Message}");
            await _logger.WriteLog("ERROR:(EX- ForwardVncToWebSocket )", $"Error forwarding VNC to WebSocket: {ex.Message}");
            await _logger.WriteLog("Exception- ForwardVncToWebSocket(OperationCanceledException)", $"{ex.ToString()}", 3);
        }
        finally
        {
            //VncProxyFileLogger.Log($"VNC to WebSocket forwarding stopped. Frames: {frameCount}, Total bytes: {totalBytesForwarded}");/
        }
    }
}