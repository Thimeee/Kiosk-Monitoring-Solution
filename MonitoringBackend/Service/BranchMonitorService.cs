using Grpc.Core;
using Monitoring.Grpc;

public class BranchMonitorService : BranchMonitor.BranchMonitorBase
{
    public override Task<BranchStatusReply> SendStatus(BranchStatusRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Received status from branch {request.BranchId}: {request.StatusMessage}, ActiveUsers={request.ActiveUsers}");
        return Task.FromResult(new BranchStatusReply
        {
            Success = true,
            Message = "Status received successfully"
        });
    }

    public override async Task StreamCommands(BranchCommandRequest request, IServerStreamWriter<BranchCommand> responseStream, ServerCallContext context)
    {
        for (int i = 0; i < 5; i++)
        {
            var cmd = new BranchCommand
            {
                CommandType = "Ping",
                CommandData = $"Ping {i} to branch {request.BranchId}"
            };
            await responseStream.WriteAsync(cmd);
            await Task.Delay(1000);
        }
    }

    public override Task<HeartbeatReply> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Heartbeat from branch {request.BranchId} at {DateTime.FromFileTimeUtc(request.Timestamp)}, ActiveUsers={request.ActiveUsers}");
        return Task.FromResult(new HeartbeatReply
        {
            Success = true,
            Message = "Heartbeat received"
        });
    }
}
