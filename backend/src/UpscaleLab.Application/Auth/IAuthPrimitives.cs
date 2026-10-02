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

public interface IEmailVerificationProvider
{
    Task<DateTime> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task ConfirmSignUpAsync(string email, string code, CancellationToken cancellationToken);

    Task ResendConfirmationCodeAsync(string email, CancellationToken cancellationToken);
}

public interface IPasswordRecoveryProvider
{
    Task StartPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task ConfirmPasswordResetAsync(
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken);
}
