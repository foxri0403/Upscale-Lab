using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace UpscaleLab.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub;
