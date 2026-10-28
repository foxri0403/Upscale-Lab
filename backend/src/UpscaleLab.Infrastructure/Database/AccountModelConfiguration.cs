using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Infrastructure.Database;

internal static class AccountModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<User>().Property(x => x.ProfileImageObjectKey).HasMaxLength(1024);
        var task = model.Entity<AccountCleanupTask>();
        task.ToTable("account_cleanup_tasks");
        task.Property(x => x.Email).HasMaxLength(320);
        task.Property(x => x.IdentitySubject).HasMaxLength(128);
        task.Property(x => x.ObjectKey).HasMaxLength(1024);
        task.Property(x => x.ObjectKeysJson).HasColumnType("jsonb");
        task.Property(x => x.LastError).HasMaxLength(200);
        task.HasIndex(x => x.NextAttemptAt);
    }
}
