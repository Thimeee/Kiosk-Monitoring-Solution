using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MonitoringBackend.SRHub
{

    //////////////////////////////Remeber This branchId repersent to TerminalId///////////////////////////////////////
    public class BranchHub : Hub
    {
        private static readonly ConcurrentDictionary<string, (string BranchId, string UserId, DateTime ConnectedAt)> ConnectionMap = new();
        private static readonly ConcurrentDictionary<string, int> BranchConnectionCount = new();
        private static readonly Timer _cleanupTimer;
        private readonly ILogger<BranchHub> _logger;

        static BranchHub()
        {
            // Cleanup stale connections every 5 minutes
            _cleanupTimer = new Timer(CleanupStaleConnections, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public BranchHub(ILogger<BranchHub> logger)
        {
            _logger = logger;
        }

        private static void CleanupStaleConnections(object state)
        {
            var staleThreshold = DateTime.Now.AddMinutes(-10);
            var staleConnections = ConnectionMap
                .Where(kvp => kvp.Value.ConnectedAt < staleThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connId in staleConnections)
            {
                if (ConnectionMap.TryRemove(connId, out var info))
                {
                    if (!string.IsNullOrWhiteSpace(info.BranchId))
                    {
                        BranchConnectionCount.AddOrUpdate(info.BranchId, 0, (key, count) => Math.Max(0, count - 1));
                    }
                }
            }

            if (staleConnections.Count > 0)
            {
                Console.WriteLine($"Cleaned up {staleConnections.Count} stale connections");
            }
        }

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            string branchId = http?.Request.Query["branchId"].ToString();
            string userId = http?.Request.Query["userId"].ToString();


            if (!string.IsNullOrWhiteSpace(branchId))
            {
                // Add to groups
                await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(branchId));
                BranchConnectionCount.AddOrUpdate(branchId, 1, (key, count) => count + 1);
            }


            if (!string.IsNullOrWhiteSpace(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
                await Groups.AddToGroupAsync(Context.ConnectionId, UserAndBranchGroup(userId, branchId));
            }

            ConnectionMap[Context.ConnectionId] = (branchId, userId, DateTime.Now);

            _logger.LogInformation(
                "Client connected - Branch: {BranchId}, User: {UserId}, Total: {Count}",
                branchId, userId, BranchConnectionCount.GetValueOrDefault(branchId, 0));

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            if (ConnectionMap.TryRemove(Context.ConnectionId, out var info))
            {
                if (!string.IsNullOrWhiteSpace(info.BranchId))
                {
                    BranchConnectionCount.AddOrUpdate(info.BranchId, 0, (key, count) => Math.Max(0, count - 1));
                }

                _logger.LogInformation(
                    "Client disconnected - Branch: {BranchId}, Remaining: {Count}",
                    info.BranchId, BranchConnectionCount.GetValueOrDefault(info.BranchId, 0));
            }

            await Clients.All.SendAsync("UserDisconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public static string BranchGroup(string branchId) => $"branch-{branchId}";
        public static string UserGroup(string userId) => $"user-{userId}";
        public static string UserAndBranchGroup(string userId, string branchId) => $"user-{userId}-branch-{branchId}";

        public Task<int> GetBranchConnectionCount(string branchId)
        {
            return Task.FromResult(BranchConnectionCount.GetValueOrDefault(branchId, 0));
        }

        public Task<int> GetTotalConnections()
        {
            return Task.FromResult(ConnectionMap.Count);
        }
    }
}