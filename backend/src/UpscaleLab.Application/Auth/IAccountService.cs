using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Auth;

public sealed record AccountProfile(Guid Id, string Email, string Username, string? ProfileImageUrl, DateTime CreatedAt);
public sealed record UpdateAccountRequest(
    [param: Required, MinLength(2), MaxLength(50)] string Username,
    [param: Required, MaxLength(128)] string CurrentPassword);
public sealed record DeleteAccountRequest(
    [param: Required, MaxLength(128)] string CurrentPassword,
    [param: Required, MaxLength(20)] string Confirmation);

public interface IAccountService
{
    Task<AccountProfile> GetAsync(Guid userId, CancellationToken ct);
    Task<AccountProfile> UpdateAsync(Guid userId, UpdateAccountRequest request, CancellationToken ct);
    Task<AccountProfile> UploadPhotoAsync(Guid userId, Stream source, CancellationToken ct);
    Task<AccountProfile> RemovePhotoAsync(Guid userId, CancellationToken ct);
    Task RequestDeletionAsync(Guid userId, DeleteAccountRequest request, CancellationToken ct);
}

public interface IAccountCleanupService
{
    Task ProcessPendingAsync(CancellationToken ct);
}

public interface IAccountIdentityRemover
{
    Task<string?> GetSubjectAsync(string email, CancellationToken ct);
    Task DeleteAsync(string subject, CancellationToken ct);
}
