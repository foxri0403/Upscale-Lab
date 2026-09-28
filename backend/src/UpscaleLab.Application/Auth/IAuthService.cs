namespace UpscaleLab.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken);
}
