using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

/// <summary>
/// Regression tests for the current monthly usage-token policy.
/// These tests do not execute real payments or database transactions.
/// </summary>
public sealed class TokenCompatibilityTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Free, 10)]
    [InlineData(SubscriptionPlan.Plus, 20)]
    [InlineData(SubscriptionPlan.Pro, 40)]
    public void MonthlyAllowance_MatchesCurrentPlan(SubscriptionPlan plan, int expected)
    {
        Assert.Equal(expected, TokenPolicy.MonthlyAllowance(plan));
    }

    [Theory]
    [InlineData(1920, 1)]
    [InlineData(2560, 2)]
    [InlineData(3840, 4)]
    public void UpscaleCost_MatchesSupportedResolutions(int longEdge, int expected)
    {
        Assert.Equal(expected, TokenPolicy.UpscaleCost(longEdge));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1280)]
    [InlineData(4096)]
    public void UpscaleCost_RejectsUnsupportedResolution(int longEdge)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenPolicy.UpscaleCost(longEdge));
    }

    [Fact]
    public void MonthlyAllowance_RejectsUnknownPlan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenPolicy.MonthlyAllowance((SubscriptionPlan)999));
    }

    [Fact]
    public void CurrentPeriodUtc_BeginsAtMidnightOnTheFirstDayOfTheUtcMonth()
    {
        // Accommodate the unlikely case that UTC changes month during the test.
        var before = DateTime.UtcNow;
        var period = TokenService.CurrentPeriodUtc();
        var after = DateTime.UtcNow;

        static DateTime FirstOfMonth(DateTime dt) =>
            new(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(DateTimeKind.Utc, period.Kind);
        Assert.True(
            period == FirstOfMonth(before) || period == FirstOfMonth(after),
            $"Expected first day of the current UTC month; actual: {period:O}");
    }

    [Fact]
    public void NewTokenWallet_HasFreePlanAndSeparatePurchasedBalance()
    {
        var wallet = new UserTokenWallet();

        Assert.Equal(SubscriptionPlan.Free, wallet.Plan);
        Assert.Equal(10, wallet.Balance);
        Assert.Equal(0, wallet.PurchasedBalance);
        Assert.Equal(DateTimeKind.Utc, wallet.PeriodStartUtc.Kind);
    }
}
