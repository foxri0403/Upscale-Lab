using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Application.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) Create(User user);
}
