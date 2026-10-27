using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Devices;
using UpscaleLab.Api.Hubs;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController(IDeviceService deviceService, IDeviceControlService control, DeviceChangeFeed changes) : ControllerBase
{
    [Authorize(Policy = "AccountSession")]
    [HttpGet("updates")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Updates(Guid? after, CancellationToken ct) =>
        Ok(new { cursor = await changes.WaitAsync(User.GetRequiredUserId(), null, after, ct) });

    [Authorize(Policy = "AccountSession")]
    [HttpPost("{id:guid}/agent-token")]
    public async Task<ActionResult<AgentTokenResponse>> RotateToken(Guid id, CancellationToken ct) =>
        Ok(await control.RotateTokenAsync(User.GetRequiredUserId(), id, ct));

    [Authorize(Policy = "AccountSession")]
    [HttpDelete("{id:guid}/agent-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        await control.RevokeTokenAsync(User.GetRequiredUserId(), id, ct);
        return NoContent();
    }

    [Authorize(Policy = "AccountSession")]
    [HttpGet("{id:guid}/state")]
    public async Task<ActionResult<DeviceState>> State(Guid id, CancellationToken ct) =>
        Ok(await control.GetStateAsync(User.GetRequiredUserId(), id, ct));

    [Authorize(Policy = "AccountSession")]
    [HttpPost("{id:guid}/commands")]
    public async Task<ActionResult<DeviceCommandResponse>> Apply(Guid id, ApplyWallpaperRequest request, CancellationToken ct) =>
        Accepted(await control.ApplyAsync(User.GetRequiredUserId(), id, request, ct));

    [Authorize(Policy = "AccountSession")]
    [HttpGet("{id:guid}/commands")]
    public async Task<ActionResult<IReadOnlyList<DeviceCommandResponse>>> Commands(Guid id, CancellationToken ct) =>
        Ok(await control.GetCommandsAsync(User.GetRequiredUserId(), id, false, ct));
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await deviceService.GetAllAsync(User.GetRequiredUserId(), cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<DeviceResponse>> Create(
        DeviceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await deviceService.CreateAsync(User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeviceResponse>> Update(
        Guid id,
        DeviceRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await deviceService.UpdateAsync(User.GetRequiredUserId(), id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deviceService.DeleteAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }
}
