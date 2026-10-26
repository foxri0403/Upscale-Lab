using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Application.Auth;
using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Api.Authentication;

public sealed class JwtTokenService(JwtOptions options) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAt) Create(User user, Guid? sessionId = null)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(options.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Name, user.Username)
        };
        if (sessionId.HasValue) claims.Add(new Claim("sid", sessionId.Value.ToString()));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
