using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Payments;

public sealed record SubscriptionCardChange(string CustomerKey, string Challenge);

public sealed class SubscriptionCardService(ApplicationDbContext db, TossBillingClient billing, BillingKeyProtector protector)
{
    public async Task<SubscriptionCardChange> StartAsync(Guid userId, CancellationToken ct)
    {
        RequireConfigured();
        var subscription = await FindRegisteredAsync(userId, ct);
        await CheckRenewalAsync(subscription.Id, userId, ct);
        var until = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var challenge = $"{subscription.Id:N}|{VersionOf(subscription)}|{until}";
        return new(subscription.CustomerKey, protector.Encrypt(challenge, Scope(userId)));
    }

    public async Task ChangeAsync(Guid userId, string challenge, string customerKey, string authKey, CancellationToken ct)
    {
        RequireConfigured();
        if (string.IsNullOrWhiteSpace(challenge) || challenge.Length > 512 || string.IsNullOrWhiteSpace(authKey) || authKey.Length > 300
            || string.IsNullOrWhiteSpace(customerKey) || customerKey.Length > 50)
            throw new ValidationException("카드 변경 인증을 다시 시작해 주세요.");

        string[] fields;
        try { fields = protector.Decrypt(challenge, Scope(userId)).Split('|'); }
        catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException)
        {
            throw new ValidationException("카드 변경 인증을 다시 시작해 주세요.");
        }
        if (fields.Length != 3 || !Guid.TryParseExact(fields[0], "N", out var id)
            || !long.TryParse(fields[2], out var until) || until <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new ValidationException("카드 변경 인증이 만료되었습니다. 다시 시도해 주세요.");

        var subscription = await FindRegisteredAsync(userId, ct);
        VerifyCurrent(subscription, id, customerKey, fields[1]);
        await CheckRenewalAsync(id, userId, ct);
        TossBillingKeyResult updated;
        try { updated = await billing.IssueBillingKeyAsync(authKey, customerKey, ct); }
        catch (Exception error) when (error is HttpRequestException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new ServiceUnavailableException("결제사의 새 카드 인증을 완료하지 못했습니다. 다시 시도해 주세요.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.UserSubscriptions.FromSqlInterpolated($"""
            SELECT * FROM user_subscriptions WHERE "Id" = {id} AND "UserId" = {userId} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        VerifyCurrent(current, id, customerKey, fields[1]);
        await CheckRenewalAsync(id, userId, ct);
        current!.BillingKeyCiphertext = protector.Encrypt(updated.BillingKey, customerKey);
        current.CardIssuerCode = updated.Card?.IssuerCode;
        current.CardNumberSuffix = updated.Card?.NumberSuffix;
        current.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<UserSubscription> FindRegisteredAsync(Guid userId, CancellationToken ct) =>
        await db.UserSubscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId &&
            x.BillingKeyCiphertext != null && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue), ct)
            ?? throw new ConflictException("변경할 자동결제 카드가 없습니다.");

    private async Task CheckRenewalAsync(Guid id, Guid userId, CancellationToken ct)
    {
        if (await db.PaymentOrders.AnyAsync(x => x.SubscriptionId == id && x.UserId == userId
            && x.Status == PaymentOrderStatus.Confirming, ct))
            throw new ConflictException("구독 결제가 처리된 뒤 카드를 변경해 주세요.");
    }

    private static void VerifyCurrent(UserSubscription? current, Guid id, string customerKey, string version)
    {
        if (current?.BillingKeyCiphertext is null || current.Id != id || current.CustomerKey != customerKey
            || current.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue) || VersionOf(current) != version)
            throw new ConflictException("등록된 카드가 변경되었습니다. 카드 변경을 다시 시작해 주세요.");
    }

    private static string VersionOf(UserSubscription subscription) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscription.BillingKeyCiphertext!)));

    private static string Scope(Guid userId) => $"subscription-card-change:{userId:N}";

    private void RequireConfigured()
    {
        if (!billing.IsConfigured) throw new ConfigurationException("카드 변경 서비스를 이용할 수 없습니다.");
    }
}
