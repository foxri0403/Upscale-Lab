using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Payments;

public sealed record AccountSubscription(string Plan, string Status, DateTime? CurrentPeriodEndUtc, bool CancelAtPeriodEnd);
public sealed record AccountPayment(string OrderId, string ProductName, int Amount, string Status, DateTime PaidAt,
    DateTime? RefundedAt, int? RefundAmount, int? RefundTokens, int RefundableAmount, int RefundableTokens, bool RefundDeclined);
public sealed record AccountBilling(AccountSubscription? Subscription, bool HasRegisteredCard, PaymentCard? Card,
    int PurchasedBalance, bool PurchasedTokensReserved, IReadOnlyList<AccountPayment> Payments);

public sealed class AccountBillingService(ApplicationDbContext db, TossPaymentsClient toss)
{
    private static readonly Dictionary<string, PaymentProduct> Products = PaymentCatalog.All.ToDictionary(x => x.Code);

    public async Task<AccountBilling> GetAsync(Guid userId, CancellationToken ct)
    {
        var subscription = await db.UserSubscriptions.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.Plan, x.Status, x.CurrentPeriodStartUtc, x.CurrentPeriodEndUtc, x.CancelAtPeriodEnd,
                Registered = x.BillingKeyCiphertext != null, x.CardIssuerCode, x.CardNumberSuffix })
            .SingleOrDefaultAsync(ct);
        var walletBalance = await db.UserTokenWallets.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.PurchasedBalance).SingleOrDefaultAsync(ct);
        var reserved = await HasPurchasedReservationAsync(userId, ct);
        var orders = await PaidOrders(userId).ToListAsync(ct);
        var balance = walletBalance;
        var history = orders.Select(order =>
        {
            var product = Products.GetValueOrDefault(order.ProductCode);
            var unused = AllocateUnused(order, product, ref balance);
            var refundable = toss.IsConfigured && !reserved && !order.RefundDeclined && order.PaymentKey != null ? unused : 0;
            return new AccountPayment(order.OrderId, product?.Name ?? order.ProductCode, order.Amount, order.Status.ToString(),
                order.PaidAt ?? order.CreatedAt, order.RefundedAt, order.RefundAmount, order.RefundTokens,
                refundable > 0 ? RefundAmount(order, product!, refundable) : 0, refundable, order.RefundDeclined);
        }).ToArray();

        var registered = subscription?.Registered == true;
        PaymentCard? card = registered && (subscription!.CardIssuerCode != null || subscription.CardNumberSuffix != null)
            ? new(subscription.CardIssuerCode, subscription.CardNumberSuffix) : null;
        if (card is null && toss.IsConfigured)
        {
            var recent = orders.FirstOrDefault(x => x.PaymentKey != null && (!registered ||
                (subscription!.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue &&
                    x.SubscriptionId == subscription.Id && x.PaidAt >= subscription.CurrentPeriodStartUtc)));
            if (recent != null)
            {
                var payment = await TryGetPaymentAsync(recent.PaymentKey!, ct);
                if (payment != null && Matches(recent, payment)) card = payment.Card;
            }
        }

        var plan = subscription is null ? null : new AccountSubscription(subscription.Plan.ToString(),
            subscription.Status.ToString(), subscription.CurrentPeriodEndUtc, subscription.CancelAtPeriodEnd);
        return new(plan, registered, card, walletBalance, reserved, history);
    }

    public async Task<PaymentOrderStatus> RefundAsync(Guid userId, string orderId, int tokens, int amount, CancellationToken ct)
    {
        if (!toss.IsConfigured) throw new ConfigurationException("환불 서비스를 이용할 수 없습니다.");
        if (tokens <= 0 || amount <= 0) throw new ValidationException("환불 수량과 금액을 확인해 주세요.");

        PaymentOrder order;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            order = await LockOrderAsync(userId, orderId, ct);
            if (order.Status is PaymentOrderStatus.Refunded or PaymentOrderStatus.Refunding)
                return order.Status;

            if (order.Status != PaymentOrderStatus.Paid || order.SubscriptionId != null ||
                order.BillingPeriodStartUtc != null || order.PaymentKey is null || order.RefundDeclined)
                throw new ConflictException("환불할 수 없는 결제입니다.");

            // The same wallet lock protects token purchases, reservations and processing refunds.
            var balance = await db.UserTokenWallets
                .FromSqlInterpolated($"""SELECT * FROM user_token_wallets WHERE "UserId" = {userId} FOR UPDATE""")
                .AsNoTracking().Select(x => x.PurchasedBalance).SingleOrDefaultAsync(ct);
            if (await HasPurchasedReservationAsync(userId, ct))
                throw new ConflictException("추가 구매 토큰을 사용 중인 작업이 끝난 뒤 다시 시도해 주세요.");

            var paidOrders = await PaidOrders(userId).ToListAsync(ct);
            var unusedForOrder = 0;
            foreach (var paid in paidOrders)
            {
                var unused = AllocateUnused(paid, Products.GetValueOrDefault(paid.ProductCode), ref balance);
                if (paid.Id != order.Id) continue;
                unusedForOrder = unused;
                break;
            }
            if (unusedForOrder == 0 || unusedForOrder != tokens || RefundAmount(order, Products[order.ProductCode], tokens) != amount)
                throw new ConflictException("환불 가능한 토큰 또는 금액이 변경되었습니다. 결제 정보를 다시 확인해 주세요.");

            await db.UserTokenWallets.Where(x => x.UserId == userId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.PurchasedBalance, x => x.PurchasedBalance - tokens)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
            order.Status = PaymentOrderStatus.Refunding;
            order.RefundTokens = tokens;
            order.RefundAmount = amount;
            order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return await CompleteRefundAsync(order, ct);
    }

    public async Task<PaymentOrderStatus> CompleteRefundAsync(PaymentOrder order, CancellationToken ct)
    {
        if (!toss.IsConfigured || order.Status != PaymentOrderStatus.Refunding ||
            order.PaymentKey is null || order.RefundAmount is not { } amount)
            return order.Status;

        var reason = "Upscale Lab 추가 구매 토큰 환불 " + order.OrderId;
        var payment = await TryGetPaymentAsync(order.PaymentKey, ct);
        if (payment is null || !Matches(order, payment)) return PaymentOrderStatus.Refunding;
        var rejected = false;
        // Read first so a completed partial refund cannot be repeated after
        // the provider's idempotency key expires.
        if (payment.Status == "DONE" && payment.Cancellations is not { Count: > 0 })
        {
            try
            {
                payment = await toss.CancelAsync(order.PaymentKey, order.OrderId, amount < order.Amount ? amount : null, reason, ct);
            }
            catch (TossCancellationRejectedException)
            {
                rejected = true;
                payment = await TryGetPaymentAsync(order.PaymentKey, ct);
            }
            catch (Exception error) when (IsTemporary(error, ct))
            {
                payment = await TryGetPaymentAsync(order.PaymentKey, ct);
            }
        }

        if (payment is null || !Matches(order, payment)) return PaymentOrderStatus.Refunding;
        var cancellation = payment.Cancellations?.FirstOrDefault(x => x.Amount == amount && x.Reason == reason);
        if (cancellation is null && !(rejected && payment.Status == "DONE")) return PaymentOrderStatus.Refunding;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await LockOrderAsync(order.UserId, order.OrderId, ct);
        if (current.Status != PaymentOrderStatus.Refunding) return current.Status;
        if (cancellation != null)
        {
            current.Status = PaymentOrderStatus.Refunded;
            current.RefundedAt = cancellation.CanceledAt.UtcDateTime;
        }
        else
        {
            await db.UserTokenWallets.Where(x => x.UserId == order.UserId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.PurchasedBalance, x => x.PurchasedBalance + current.RefundTokens!.Value)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
            current.Status = PaymentOrderStatus.Paid;
            current.RefundDeclined = true;
            current.RefundAmount = null;
            current.RefundTokens = null;
        }
        current.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return current.Status;
    }

    private IQueryable<PaymentOrder> PaidOrders(Guid userId) => db.PaymentOrders.AsNoTracking()
        .Where(x => x.UserId == userId && (x.Status == PaymentOrderStatus.Paid ||
            x.Status == PaymentOrderStatus.Refunding || x.Status == PaymentOrderStatus.Refunded))
        .OrderByDescending(x => x.PaidAt ?? x.CreatedAt).ThenByDescending(x => x.OrderId);

    private Task<bool> HasPurchasedReservationAsync(Guid userId, CancellationToken ct) => db.TokenTransactions
        .AnyAsync(x => x.UserId == userId && x.Status == TokenTransactionStatus.Reserved && x.PurchasedAmount > 0, ct);

    // Purchased tokens have historically been stored in a single wallet. Apply
    // spending oldest-first, so newer purchases never make older spending refundable.
    private static int AllocateUnused(PaymentOrder order, PaymentProduct? product, ref int balance)
    {
        if (order.Status != PaymentOrderStatus.Paid || order.SubscriptionId != null || order.BillingPeriodStartUtc != null ||
            product is not { IsSubscription: false }) return 0;
        var unused = Math.Clamp(balance, 0, product.Tokens);
        balance -= unused;
        return unused;
    }

    private static int RefundAmount(PaymentOrder order, PaymentProduct product, int tokens) =>
        (int)((long)order.Amount * tokens / product.Tokens);

    private async Task<PaymentOrder> LockOrderAsync(Guid userId, string orderId, CancellationToken ct)
    {
        // Reload an order previously tracked during the claim, so a competing recovery
        // can never have its committed outcome overwritten or return tokens twice.
        var tracked = db.PaymentOrders.Local.FirstOrDefault(x => x.OrderId == orderId && x.UserId == userId);
        if (tracked is not null) db.Entry(tracked).State = EntityState.Detached;
        return await db.PaymentOrders.FromSqlInterpolated($"""
            SELECT * FROM payment_orders WHERE "OrderId" = {orderId} AND "UserId" = {userId} FOR UPDATE
            """).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("결제 내역을 찾을 수 없습니다.");
    }

    private async Task<TossPaymentResult?> TryGetPaymentAsync(string key, CancellationToken ct)
    {
        try { return await toss.GetPaymentAsync(key, ct); }
        catch (Exception error) when (IsTemporary(error, ct)) { return null; }
    }

    private static bool Matches(PaymentOrder order, TossPaymentResult payment) =>
        payment.OrderId == order.OrderId && payment.PaymentKey == order.PaymentKey && payment.TotalAmount == order.Amount;

    private static bool IsTemporary(Exception error, CancellationToken ct) =>
        !ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or JsonException
            or KeyNotFoundException or InvalidOperationException;
}
