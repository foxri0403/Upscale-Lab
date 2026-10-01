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

public interface IEmailSender
{
    Task SendVerificationCodeAsync(
        string recipientEmail,
        string recipientName,
        string code,
        int expiresInMinutes,
        CancellationToken cancellationToken);
}

public interface IEmailVerificationCodeProtector
{
    string Generate();
    string Hash(string email, string code);
    bool Verify(string email, string code, string expectedHash);
}
