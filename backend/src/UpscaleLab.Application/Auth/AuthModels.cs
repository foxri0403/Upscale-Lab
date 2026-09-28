using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Auth;

public sealed record RegisterRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, MinLength(2), MaxLength(50)] string Username,
    [param: Required, MinLength(8), MaxLength(128)] string Password);

public sealed record LoginRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, MaxLength(128)] string Password);

public sealed record UserResponse(Guid Id, string Email, string Username, DateTime CreatedAt);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);
