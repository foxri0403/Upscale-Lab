namespace UpscaleLab.Application.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task ResendVerificationEmailAsync(
        ResendVerificationEmailRequest request,
        CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken);
}
