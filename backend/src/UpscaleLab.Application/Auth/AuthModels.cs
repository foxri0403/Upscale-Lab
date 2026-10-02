using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Auth;

public sealed record RegisterRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, MinLength(2), MaxLength(50)] string Username,
    [param: Required, PasswordPolicy] string Password);

public sealed record LoginRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, MaxLength(128)] string Password);

public sealed record VerifyEmailRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record ResendVerificationEmailRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email);

public sealed record FindIdRequest(
    [param: Required, MinLength(2), MaxLength(50)] string Username);

public sealed record FindIdResponse(string? MaskedEmail);

public sealed record PasswordResetStartRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email);

public sealed record PasswordResetConfirmRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, RegularExpression("^[0-9]{6}$")] string Code,
    [param: Required, PasswordPolicy] string NewPassword);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string Username,
    bool IsEmailVerified,
    DateTime CreatedAt);

public sealed record RegisterResponse(
    UserResponse User,
    bool RequiresEmailVerification,
    DateTime VerificationCodeExpiresAt);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);

public sealed record MessageResponse(string Message);
