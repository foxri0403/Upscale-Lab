using Microsoft.EntityFrameworkCore;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

internal static class AuthUserTransaction
{
    // Verification, resend and expiration share a PostgreSQL row lock. Cleanup
    // cannot delete an account while its confirmation request is in flight.
    public static async Task<T> RunAsync<T>(ApplicationDbContext db, string email,
        Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return await action();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM users WHERE \"Email\" = {email} FOR UPDATE", cancellationToken);
            foreach (var entry in db.ChangeTracker.Entries<Domain.Entities.User>()
                         .Where(x => x.Entity.Email == email).ToList())
                await entry.ReloadAsync(cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
