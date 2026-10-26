using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace UpscaleLab.Api.Hubs;

[Authorize(Policy = "AccountSession")]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var sessionId = Context.User!.FindFirst("sid")!.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}", Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
