using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Monitoring.Shared.Models;

namespace MonitoringBackend.SRHub
{
    public class BranchHub : Hub
    {
        // Optional: global connection -> (branchId,userId) map for server-side lookups
        public static ConcurrentDictionary<string, (string BranchId, string UserId)> ConnectionMap = new();

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            string branchId = http.Request.Query["branchId"];
            string userId = http.Request.Query["userId"];

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(branchId));
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            }

            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(branchId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserAndBranchGroup(userId, branchId));

            }

            //await Clients.All.SendAsync("UserConnected", object);


            ConnectionMap[Context.ConnectionId] = (branchId, userId);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            ConnectionMap.TryRemove(Context.ConnectionId, out _);
            await Clients.All.SendAsync("UserDisconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public static string BranchGroup(string branchId) => $"branch-{branchId}";
        public static string UserGroup(string userId) => $"user-{userId}";
        public static string UserAndBranchGroup(string userId, string branchId) => $"user-{userId}-branch-{branchId}";
    }
}
