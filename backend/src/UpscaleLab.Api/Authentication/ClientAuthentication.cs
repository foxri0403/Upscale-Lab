using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Application.Auth;

namespace UpscaleLab.Api.Authentication;

public static class ClientAuthentication
{
    public static IServiceCollection AddClientAuthentication(this IServiceCollection services, JwtOptions jwt, bool includeDeviceAgents = true)
    {
        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs/notifications") &&
                            context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var sid = context.Principal?.FindFirst("sid")?.Value;
                        // Legacy JWTs retain existing API access; new control/agent APIs require sid.
                        if (sid is null) return;
                        var validator = context.HttpContext.RequestServices.GetRequiredService<IActiveSessionValidator>();
                        if (!Guid.TryParse(sid, out var sessionId) || context.Principal is null ||
                            !await validator.IsActiveAsync(context.Principal.GetRequiredUserId(), sessionId, context.HttpContext.RequestAborted))
                            context.Fail("로그인 세션이 만료되거나 해제되었습니다.");
                    }
                };
            });
        if (includeDeviceAgents)
            authentication.AddScheme<AuthenticationSchemeOptions, DeviceAgentHandler>(DeviceAgentHandler.SchemeName, _ => { });
        services.AddAuthorization(options => options.AddPolicy("AccountSession", policy =>
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().RequireClaim("sid")));
        return services;
    }
}
