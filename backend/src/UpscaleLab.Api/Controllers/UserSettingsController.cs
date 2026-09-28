using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Settings;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class UserSettingsController(IUserSettingService settingService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserSettingResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await settingService.GetAsync(User.GetRequiredUserId(), cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<UserSettingResponse>> Update(
        UpdateUserSettingRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await settingService.UpdateAsync(User.GetRequiredUserId(), request, cancellationToken));
    }
}
