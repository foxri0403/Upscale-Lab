using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Tokens;

public sealed class TokenService(ApplicationDbContext db) : ITokenService
{
    public static DateTime CurrentPeriodUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(
            now.Year, now.Month, 1,
            0, 0, 0, DateTimeKind.Utc);
    }


    public async Task<UserTokenWallet> GetOrRefreshWalletAsync(
        Guid userId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx =
                await db.Database.BeginTransactionAsync(ct);

            var now = DateTime.UtcNow;

            var effectivePlan = await GetEffectivePlanAsync(
                userId, now, ct);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO user_token_wallets
                    ("Id", "UserId", "Plan", "Balance",
                     "PurchasedBalance", "PeriodStartUtc",
                     "CreatedAt", "UpdatedAt")
                VALUES
                    ({Guid.NewGuid()}, {userId}, 'Free', 10,
                     0, {now}, {now}, {now})
                ON CONFLICT ("UserId") DO NOTHING
                """, ct);


            var wallet = await db.UserTokenWallets
                .FromSqlInterpolated($"""
                    SELECT * FROM user_token_wallets
                    WHERE "UserId" = {userId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleAsync(ct);

            var nextPeriod = TokenPolicy.WeeklyPeriodStart(
                wallet.PeriodStartUtc, now);

            var planChanged = wallet.Plan != effectivePlan;

            if (planChanged || nextPeriod > wallet.PeriodStartUtc)
            {
                var allowance =
                    TokenPolicy.WeeklyAllowance(effectivePlan);

                if (planChanged)
                    nextPeriod = now;

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE user_token_wallets
                    SET "Plan" = {effectivePlan.ToString()},
                        "Balance" = {allowance},
                        "PeriodStartUtc" = {nextPeriod},
                        "UpdatedAt" = {now}
                    WHERE "UserId" = {userId}
                    """, ct);

                wallet.Plan = effectivePlan;
                wallet.Balance = allowance;
                wallet.PeriodStartUtc = nextPeriod;
            }

            await tx.CommitAsync(ct);
            return wallet;
        });
    }


    // 반드시 DB 트랜잭션 안에서 호출한다.
    // 구독 잠금 -> 지갑 잠금 순서를 유지해야 한다.
    private async Task<SubscriptionPlan> GetEffectivePlanAsync(
        Guid userId,
        DateTime now,
        CancellationToken ct)
    {
        RequireTransaction();

        var subscription = await db.UserSubscriptions
            .FromSqlInterpolated($"""
                SELECT * FROM user_subscriptions
                WHERE "UserId" = {userId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (subscription is null ||
            subscription.Status != SubscriptionStatus.Active ||
            subscription.CurrentPeriodStartUtc is null ||
            subscription.CurrentPeriodEndUtc is null ||
            subscription.CurrentPeriodStartUtc > now ||
            subscription.CurrentPeriodEndUtc <= now)
        {
            return SubscriptionPlan.Free;
        }

        return subscription.Plan is SubscriptionPlan.Plus
            or SubscriptionPlan.Pro
            ? subscription.Plan
            : SubscriptionPlan.Free;
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "토큰 처리에는 DB 트랜잭션이 필요합니다.");
    }

    public async Task ReserveAsync(
        Guid userId,
        Guid jobId,
        int targetLongEdge,
        CancellationToken ct)
    {
        RequireTransaction();

        var cost = TokenPolicy.UpscaleCost(targetLongEdge);
        var now = DateTime.UtcNow;
        var period = now;

        // 사용자 지갑이 없으면 Free 플랜으로 생성
        var effectivePlan = await GetEffectivePlanAsync(
            userId, now, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_token_wallets
                ("Id", "UserId", "Plan", "Balance",
                 "PurchasedBalance", "PeriodStartUtc",
                 "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {userId}, 'Free', 10,
                 0, {period}, {now}, {now})
            ON CONFLICT ("UserId") DO NOTHING
            """, ct);


        // 지갑 행 잠금: 동일 사용자 동시 요청 직렬화
        var wallet = await db.UserTokenWallets
            .FromSqlInterpolated($"""
                SELECT * FROM user_token_wallets
                WHERE "UserId" = {userId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleAsync(ct);

        // 모든 플랜은 각자의 지급 시작일 기준으로 7일마다 갱신한다.
        var effectivePeriod = TokenPolicy.WeeklyPeriodStart(
            wallet.PeriodStartUtc, now);

        var planChanged = wallet.Plan != effectivePlan;
        if (planChanged)
            effectivePeriod = now;

        var weeklyRenewal =
            planChanged || effectivePeriod > wallet.PeriodStartUtc;

        var monthlyBalance = weeklyRenewal
            ? TokenPolicy.WeeklyAllowance(effectivePlan)
            : wallet.Balance;

        var monthlyUsed = Math.Min(monthlyBalance, cost);
        var purchasedUsed = cost - monthlyUsed;

        if (wallet.PurchasedBalance < purchasedUsed)
            throw new UpscaleLab.Application.Common.ValidationException(
                $"토큰이 부족합니다. 필요한 토큰: {cost}개");

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE user_token_wallets
            SET "Plan" = {effectivePlan.ToString()},
                "Balance" = {monthlyBalance - monthlyUsed},
                "PurchasedBalance" =
                    "PurchasedBalance" - {purchasedUsed},
                "PeriodStartUtc" = {effectivePeriod},
                "UpdatedAt" = {now}
            WHERE "UserId" = {userId}
            """, ct);

        db.TokenTransactions.Add(new TokenTransaction
        {
            UserId = userId,
            ProcessingJobId = jobId,
            Amount = cost,
            MonthlyAmount = monthlyUsed,
            PurchasedAmount = purchasedUsed,
            PeriodStartUtc = effectivePeriod,
            Status = TokenTransactionStatus.Reserved
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(
        Guid jobId,
        CancellationToken ct)
    {
        RequireTransaction();

        var updated = await db.TokenTransactions
            .Where(x => x.ProcessingJobId == jobId &&
                        x.Status == TokenTransactionStatus.Reserved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Status,
                    TokenTransactionStatus.Completed)
                .SetProperty(
                    x => x.CompletedAt,
                    DateTime.UtcNow),
                ct);

        if (updated == 1)
            return;

        // 재시도 상황에서 이미 완료된 동일 거래는 중복 확정하지 않는다.
        var status = await db.TokenTransactions
            .AsNoTracking()
            .Where(x => x.ProcessingJobId == jobId)
            .Select(x => (TokenTransactionStatus?)x.Status)
            .SingleOrDefaultAsync(ct);

        if (status == TokenTransactionStatus.Completed)
            return;

        throw new InvalidOperationException(
            $"작업 {jobId}의 토큰 거래를 완료 상태로 확정할 수 없습니다.");
    }

    public async Task RefundAsync(
        Guid jobId,
        CancellationToken ct)
    {
        RequireTransaction();

        var transaction = await db.TokenTransactions
            .FromSqlInterpolated($"""
                SELECT * FROM token_transactions
                WHERE "ProcessingJobId" = {jobId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (transaction is null ||
            transaction.Status != TokenTransactionStatus.Reserved)
            return;

        var now = DateTime.UtcNow;
        var period = CurrentPeriodUtc();

        // 구매 토큰은 언제나 반환
        // 월간 토큰은 같은 지급 월인 경우에만 반환
        var walletUpdated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE user_token_wallets
            SET "PurchasedBalance" =
                    "PurchasedBalance" + {transaction.PurchasedAmount},
                "Balance" =
                    CASE
                        WHEN "PeriodStartUtc" =
                             {transaction.PeriodStartUtc}
                        THEN "Balance" + {transaction.MonthlyAmount}
                        ELSE "Balance"
                    END,
                "UpdatedAt" = {now}
            WHERE "UserId" = {transaction.UserId}
            """, ct);

        var transactionUpdated = await db.TokenTransactions
            .Where(x => x.Id == transaction.Id &&
                        x.Status == TokenTransactionStatus.Reserved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Status,
                    TokenTransactionStatus.Refunded)
                .SetProperty(
                    x => x.CompletedAt,
                    now),
                ct);
        if (walletUpdated != 1 || transactionUpdated != 1)
        {
            throw new InvalidOperationException(
                $"작업 {jobId}의 토큰 환불 처리 결과가 올바르지 않습니다. " +
                $"지갑 갱신: {walletUpdated}, 거래 갱신: {transactionUpdated}");
        }

    }
}
