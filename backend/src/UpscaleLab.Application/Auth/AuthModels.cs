using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Auth;

public sealed record RegisterRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, MinLength(2), MaxLength(50), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "아이디는 영문과 숫자만 이용 가능합니다.")] string Username,
    [param: Required, PasswordPolicy] string Password);

public sealed record LoginRequest(
    // Backward-compatible field name: accepts either an email address or username.
    [param: Required, MinLength(2), MaxLength(320)] string Email,
    [param: Required, MaxLength(128)] string Password);

public sealed record VerifyEmailRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record ResendVerificationEmailRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email);

public sealed record CancelRegistrationRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: StringLength(64, MinimumLength = 64)] string? Token = null,
    [param: MaxLength(128)] string? Password = null);

public sealed record FindIdRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email);

public sealed record FindIdResponse(string? Username);

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
    string Email,
    string Username,
    bool RequiresEmailVerification,
    DateTime VerificationCodeSentAt,
    DateTime VerificationCodeExpiresAt,
    DateTime ResendAvailableAt,
    DateTime ServerTime,
    string CancellationToken,
    RegistrationChallenge? Registration = null);

public sealed record RegistrationChallenge(string Email, string Username, string Token,
    DateTime ExpiresAt, string TermsVersion);

public sealed record AcceptRegistrationTermsRequest(
    [param: Required, EmailAddress, MaxLength(320)] string Email,
    [param: Required, StringLength(64, MinimumLength = 64)] string Token,
    [param: Required, MaxLength(32)] string TermsVersion,
    bool AcceptTerms,
    bool ConfirmSignature,
    [param: Required, MaxLength(50)] string Signature);

public sealed record VerificationEmailResponse(string Message, DateTime? VerificationCodeSentAt,
    DateTime? VerificationCodeExpiresAt, DateTime? ResendAvailableAt, DateTime ServerTime);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User,
    string? RefreshToken = null, DateTime? RefreshExpiresAt = null);

public sealed record RefreshSessionRequest([param: Required, MaxLength(128)] string RefreshToken);

public sealed record MessageResponse(string Message);
