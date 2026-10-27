using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Devices;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Api.Hubs;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = DeviceAgentHandler.SchemeName)]
[Route("api/agent")]
public sealed class AgentController(IDeviceControlService devices, IWallpaperService wallpapers, DeviceChangeFeed changes) : ControllerBase
{
    private AgentIdentity Identity => new(Guid.Parse(User.FindFirst("device_id")!.Value), User.GetRequiredUserId());

    [HttpGet("updates")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Updates(Guid? after, CancellationToken ct) =>
        Ok(new { cursor = await changes.WaitAsync(Identity.UserId, Identity.DeviceId, after, ct) });

    [HttpGet("state")]
    public async Task<ActionResult<DeviceState>> State(CancellationToken ct) =>
        Ok(await devices.GetStateAsync(Identity.UserId, Identity.DeviceId, ct));

    [HttpPost("heartbeat")]
    public async Task<ActionResult<DeviceState>> Heartbeat(AgentHeartbeat request, CancellationToken ct) =>
        Ok(await devices.HeartbeatAsync(Identity, request, ct));

    [HttpGet("commands")]
    public async Task<ActionResult<IReadOnlyList<DeviceCommandResponse>>> Commands(CancellationToken ct) =>
        Ok(await devices.GetCommandsAsync(Identity.UserId, Identity.DeviceId, true, ct));

    [HttpPost("commands/{id:guid}/report")]
    public async Task<ActionResult<DeviceCommandResponse>> Report(Guid id, CommandReport request, CancellationToken ct) =>
        Ok(await devices.ReportAsync(Identity, id, request, ct));

    [HttpGet("commands/{id:guid}/manifest")]
    public async Task<ActionResult<WallpaperManifest>> Manifest(Guid id, CancellationToken ct)
    {
        var assetId = await devices.GetCommandAssetAsync(Identity, id, ct);
        return Ok(await wallpapers.GetManifestAsync(Identity.UserId, assetId, true, ct));
    }
}
