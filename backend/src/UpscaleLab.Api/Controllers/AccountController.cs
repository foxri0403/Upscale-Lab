using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Auth;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize(Policy = "AccountSession")]
[Route("api/account")]
public sealed class AccountController(IAccountService account) : ControllerBase
{
    [HttpGet]
    public Task<AccountProfile> Get(CancellationToken ct) => account.GetAsync(User.GetRequiredUserId(), ct);

    [HttpPut]
    [EnableRateLimiting("account-sensitive")]
    public Task<AccountProfile> Update(UpdateAccountRequest request, CancellationToken ct) => account.UpdateAsync(User.GetRequiredUserId(), request, ct);

    [HttpPost("photo")]
    [EnableRateLimiting("account-sensitive")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<AccountProfile>> UploadPhoto(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > 5 * 1024 * 1024) return ValidationProblem("프로필 사진은 0바이트 초과, 5MB 이하로 선택해 주세요.");
        await using var source = file.OpenReadStream();
        return Ok(await account.UploadPhotoAsync(User.GetRequiredUserId(), source, ct));
    }

    [HttpDelete("photo")]
    [EnableRateLimiting("account-sensitive")]
    public Task<AccountProfile> RemovePhoto(CancellationToken ct) => account.RemovePhotoAsync(User.GetRequiredUserId(), ct);

    [HttpDelete]
    [EnableRateLimiting("account-sensitive")]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken ct)
    {
        await account.RequestDeletionAsync(User.GetRequiredUserId(), request, ct);
        return Accepted(new MessageResponse("계정 삭제 요청을 접수했습니다. 계정은 즉시 잠기며 저장 데이터가 정리됩니다."));
    }
}
