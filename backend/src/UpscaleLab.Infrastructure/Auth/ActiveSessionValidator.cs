using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class ActiveSessionValidator(ApplicationDbContext db, TimeProvider clock) : IActiveSessionValidator
{
    public async Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.AuthSessions.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(
            x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now &&
                x.CreatedAt > now.AddDays(-365), ct);
        return session is not null && session.User.IsEmailVerified && session.User.DeletionRequestedAt == null && session.PasswordFingerprint ==
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.User.PasswordHash)));
    }
}
