using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using UpscaleLab.Application.Devices;

namespace UpscaleLab.Api.Authentication;

public sealed class DeviceAgentHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IDeviceControlService devices) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceAgent";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var account = await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!account.Succeeded || account.Principal is null ||
            !Guid.TryParse(account.Principal.FindFirstValue("sid"), out _))
            return AuthenticateResult.Fail("유효한 계정 로그인 세션이 필요합니다.");
        var identity = await devices.AuthenticateAsync(Request.Headers["X-Device-Token"].ToString(), Context.RequestAborted);
        if (identity is null || account.Principal.GetRequiredUserId() != identity.UserId)
            return AuthenticateResult.Fail("로그인 계정과 연결된 기기 자격이 일치하지 않습니다.");
        var claims = new ClaimsIdentity(account.Principal.Claims, SchemeName);
        claims.AddClaim(new Claim("device_id", identity.DeviceId.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), SchemeName));
    }
}
