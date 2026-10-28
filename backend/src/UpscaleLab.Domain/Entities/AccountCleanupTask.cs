namespace UpscaleLab.Domain.Entities;

// No user FK: cleanup must survive deleting the account itself.
public sealed class AccountCleanupTask : BaseEntity
{
    public Guid UserId { get; set; }
    public bool DeleteAccount { get; set; }
    public string? Email { get; set; }
    public string? IdentitySubject { get; set; }
    public string? ObjectKey { get; set; }
    public string? ObjectKeysJson { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? FirstSweepAt { get; set; }
    public string? LastError { get; set; }
}
