using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Auth;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class AccountServiceTests
{
    private const string Password = "AccountTest123!";
    private static User User(PasswordHasher h, string name = "owner") => new() { Email = name + "@example.invalid", Username = name, IsEmailVerified = true, PasswordHash = h.Hash(Password) };
    private sealed class Identity : IAccountIdentityRemover
    {
        public bool Fail; public List<string> Deleted = [];
        public Task<string?> GetSubjectAsync(string email, CancellationToken ct) => Task.FromResult<string?>("immutable-sub");
        public Task DeleteAsync(string subject, CancellationToken ct) { if (Fail) throw new IOException(); Deleted.Add(subject); return Task.CompletedTask; }
    }
    [Fact]
    public async Task Rename_RequiresPasswordAndUniqueName_ChangesOnlyOwner()
    {
        await using var db = WorkflowData.Db(); var h = new PasswordHasher(); var user = User(h); var other = User(h, "other"); db.AddRange(user, other); await db.SaveChangesAsync();
        var service = new AccountService(db, h, new WorkflowStore(), new Identity());
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(user.Id, new("changed", "wrong"), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(user.Id, new("other", Password), default));
        var profile = await service.UpdateAsync(user.Id, new("changed", Password), default);
        Assert.Equal("changed", profile.Username); Assert.Equal("other", other.Username);
    }
    [Fact]
    public async Task Photo_NormalizesSizeAndFormat_QueuesOldObject_RejectsNonImages()
    {
        await using var db = WorkflowData.Db(); var h = new PasswordHasher(); var user = User(h); db.Add(user); await db.SaveChangesAsync();
        var store = new WorkflowStore(); var service = new AccountService(db, h, store, new Identity());
        using var image = new MagickImage(MagickColors.Red, 1024, 768); using var photo = new MemoryStream(image.ToByteArray(MagickFormat.Jpeg));
        await service.UploadPhotoAsync(user.Id, photo, default); var old = user.ProfileImageObjectKey!;
        using var result = new MagickImage(store.Files[old]); Assert.Equal(512u, result.Width); Assert.Equal(MagickFormat.Png, result.Format);
        await service.RemovePhotoAsync(user.Id, default); Assert.Null(user.ProfileImageObjectKey); Assert.Equal(old, (await db.AccountCleanupTasks.SingleAsync()).ObjectKey);
        await Assert.ThrowsAsync<ValidationException>(() => service.UploadPhotoAsync(user.Id, new MemoryStream([1, 2, 3]), default));
        await new AccountCleanupService(db, store, new Identity()).ProcessPendingAsync(default); Assert.Empty(store.Files); Assert.Empty(db.AccountCleanupTasks);
    }
    [Fact]
    public async Task Deletion_RequiresConfirmationAndPassword_RevokesSessionsThenRetriesDurably()
    {
        await using var db = WorkflowData.Db(); var h = new PasswordHasher(); var owner = User(h); var other = User(h, "other");
        var session = new AuthSession { UserId = owner.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) }; var key = $"users/{owner.Id:N}/profile/old.png"; owner.ProfileImageObjectKey = key;
        db.AddRange(owner, other, session); await db.SaveChangesAsync();
        var store = new WorkflowStore(); store.Files[key] = [1]; store.Files[$"users/{other.Id:N}/keep.png"] = [2]; var identity = new Identity(); var clock = new WorkflowClock();
        var service = new AccountService(db, h, store, identity, clock);
        await Assert.ThrowsAsync<ValidationException>(() => service.RequestDeletionAsync(owner.Id, new(Password, "삭제"), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.RequestDeletionAsync(owner.Id, new("wrong", "계정 삭제"), default));
        Assert.Null(owner.DeletionRequestedAt); Assert.Empty(db.AccountCleanupTasks);
        await service.RequestDeletionAsync(owner.Id, new(Password, "계정 삭제"), default);
        Assert.NotNull(owner.DeletionRequestedAt); Assert.NotNull(session.RevokedAt); Assert.False(h.Verify(Password, owner.PasswordHash));
        Assert.Equal(other.Id, (await db.Users.SingleAsync()).Id);
        Assert.Empty(db.AuthSessions);
        Assert.Contains(key, (await db.AccountCleanupTasks.SingleAsync()).ObjectKeysJson!);
        clock.Time = clock.Time.AddMinutes(1); identity.Fail = true;
        await new AccountCleanupService(db, store, identity, clock).ProcessPendingAsync(default);
        Assert.Single(db.Users); Assert.Equal(1, (await db.AccountCleanupTasks.SingleAsync()).Attempts);
        identity.Fail = false; clock.Time = clock.Time.AddHours(1);
        await new AccountCleanupService(db, store, identity, clock).ProcessPendingAsync(default);
        Assert.Equal(other.Id, (await db.Users.SingleAsync()).Id); Assert.Single(store.Files); Assert.Empty(db.AuthSessions); Assert.Single(identity.Deleted);
        var replacement = User(h, "owner"); db.Add(replacement); await db.SaveChangesAsync(); clock.Time = clock.Time.AddMinutes(11);
        await new AccountCleanupService(db, store, identity, clock).ProcessPendingAsync(default);
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Single(identity.Deleted); Assert.Empty(db.AccountCleanupTasks);
    }
    [Fact]
    public async Task Deletion_RemovesPurchaseAndSubscriptionData_BeforeExternalCleanup()
    {
        await using var db = WorkflowData.Db(); var h = new PasswordHasher();
        var owner = User(h); var other = User(h, "other");
        var subscription = new UserSubscription { UserId = owner.Id, CustomerKey = "owner-customer", BillingKeyCiphertext = "encrypted" };
        var order = new PaymentOrder { UserId = owner.Id, SubscriptionId = subscription.Id, OrderId = "owner-order", Amount = 1000 };
        var otherOrder = new PaymentOrder { UserId = other.Id, OrderId = "other-order", Amount = 1000 };
        db.AddRange(owner, other, subscription, order, otherOrder, new UserTokenWallet { UserId = owner.Id });
        await db.SaveChangesAsync();
        var identity = new Identity { Fail = true };
        await new AccountService(db, h, new WorkflowStore(), identity).RequestDeletionAsync(owner.Id, new(Password, "계정 삭제"), default);
        Assert.Equal(other.Id, (await db.Users.SingleAsync()).Id);
        Assert.Equal(otherOrder.Id, (await db.PaymentOrders.SingleAsync()).Id);
        Assert.Empty(db.UserSubscriptions); Assert.Empty(db.UserTokenWallets);
        Assert.Empty(identity.Deleted); Assert.Single(db.AccountCleanupTasks);
        // A replacement account must survive retries targeting the immutable old subject.
        var replacement = User(h); db.Add(replacement); await db.SaveChangesAsync();
        var cleanup = new AccountCleanupService(db, new WorkflowStore(), identity);
        await cleanup.ProcessTaskAsync((await db.AccountCleanupTasks.SingleAsync()).Id, default);
        Assert.Equal(2, await db.Users.CountAsync());
        identity.Fail = false;
        await cleanup.ProcessTaskAsync((await db.AccountCleanupTasks.SingleAsync()).Id, default);
        Assert.True(await db.Users.AnyAsync(x => x.Id == replacement.Id)); Assert.Single(identity.Deleted);
    }
    [Fact]
    public void PasswordChanges_AlwaysUseFreshBcryptSalt()
    {
        var h = new PasswordHasher(); var first = h.Hash(Password); var second = h.Hash(Password);
        Assert.NotEqual(first, second); Assert.True(h.Verify(Password, second)); Assert.False(h.Verify("wrong", second));
    }
}
