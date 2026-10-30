using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class TokenCompatibilityTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Free, 10)]
    [InlineData(SubscriptionPlan.Plus, 20)]
    [InlineData(SubscriptionPlan.Pro, 40)]
    public void PreservesDeployedWeeklyAllowances(SubscriptionPlan plan, int tokens) =>
        Assert.Equal(tokens, TokenPolicy.WeeklyAllowance(plan));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 7)]
    [InlineData(15, 14)]
    public void WeeklyPeriodsFollowTheUserStartRatherThanCalendarMonth(int days, int periodDays)
    {
        var start = new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc);
        Assert.Equal(start.AddDays(periodDays), TokenPolicy.WeeklyPeriodStart(start, start.AddDays(days)));
    }

    [Fact]
    public void DeletingJobsPreservesTokenHistory()
    {
        using var db = WorkflowData.Db();
        var entity = db.Model.FindEntityType(typeof(TokenTransaction))!;
        Assert.True(entity.FindProperty(nameof(TokenTransaction.ProcessingJobId))!.IsNullable);
        Assert.Equal(DeleteBehavior.SetNull, entity.GetForeignKeys().Single(x => x.Properties.Any(p => p.Name == nameof(TokenTransaction.ProcessingJobId))).DeleteBehavior);
    }
}
