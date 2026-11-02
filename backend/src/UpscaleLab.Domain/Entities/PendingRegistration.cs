namespace UpscaleLab.Domain.Entities;

// Temporary verification state. This is not a member and has no settings,
// sessions, wallets, images or other member-owned resources.
public sealed class PendingRegistration : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime VerificationSentAt { get; set; }
    public DateTime VerificationExpiresAt { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public string? ConsentTokenHash { get; set; }
    public DateTime? ConsentTokenExpiresAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
