namespace UpscaleLab.Application.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<RegistrationChallenge> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> AcceptRegistrationTermsAsync(AcceptRegistrationTermsRequest request, CancellationToken cancellationToken);
    Task CancelRegistrationAsync(CancelRegistrationRequest request, CancellationToken cancellationToken);
    Task<VerificationEmailResponse> ResendVerificationEmailAsync(
        ResendVerificationEmailRequest request,
        CancellationToken cancellationToken);
    Task<FindIdResponse> FindIdAsync(FindIdRequest request, CancellationToken cancellationToken);
    Task StartPasswordResetAsync(
        PasswordResetStartRequest request,
        CancellationToken cancellationToken);
    Task ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest request,
        CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshSessionRequest request, CancellationToken cancellationToken);
    Task RevokeSessionAsync(RefreshSessionRequest request, CancellationToken cancellationToken);
    Task DeleteExpiredRegistrationsAndSessionsAsync(CancellationToken cancellationToken);
    Task<UserResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken);
}
