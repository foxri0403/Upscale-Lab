using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Devices;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController(IDeviceService deviceService) : ControllerBase
{
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
