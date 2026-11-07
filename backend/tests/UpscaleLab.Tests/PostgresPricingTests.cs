using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresPricingTests
{
    [Theory]
    [Trait("Category", "PostgreSQLIntegration")]
    [InlineData(ProcessingOperation.Upscale, 1920, 2, 1)]
    [InlineData(ProcessingOperation.Upscale, 2560, 4, 2)]
    [InlineData(ProcessingOperation.Upscale, 3840, 6, 3)]
    [InlineData(ProcessingOperation.Layers, null, 3, 1)]
    [InlineData(ProcessingOperation.CharacterLayers, null, 7, 3)]
    public async Task ApprovedRates_ReserveAndRefundRealWalletBalances(
        ProcessingOperation operation, int? edge, int firstCost, int? revisionCost)
    {
        var connection = Environment.GetEnvironmentVariable("UPSCALELAB_TOKEN_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        Assert.Equal("upscalelab_token_test", new Npgsql.NpgsqlConnectionStringBuilder(connection).Database);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var user = new User { Email = $"pricing-{Guid.NewGuid():N}@example.invalid",
            Username = "pricing-" + Guid.NewGuid().ToString("N")[..12], PasswordHash = "test-only" };
        var project = WorkflowData.Project(user.Id);
        var imageId = project.OriginalImage.Id;
        db.Add(user);
        db.Add(project);
        db.UserTokenWallets.Add(new UserTokenWallet { UserId = user.Id,
            Plan = SubscriptionPlan.Pro, Balance = 40, PeriodStartUtc = TokenService.CurrentPeriodUtc() });
        await db.SaveChangesAsync();
        var tokens = new TokenService(db);
        var service = new ProjectProcessingService(db, new WorkflowStore(), new StartOnlyProcessor(),
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance, tokens);
        var request = new ProcessProjectRequest(operation, TargetLongEdge: edge);
        async Task<int> Balance() => (await db.UserTokenWallets.AsNoTracking().SingleAsync(x => x.UserId == user.Id)).Balance;
        try
        {
            var first = await service.StartAsync(user.Id, project.Id, request, default);
            Assert.Equal(40 - firstCost, await Balance());
            Assert.Equal(firstCost, (await db.TokenTransactions.AsNoTracking().SingleAsync(x => x.ProcessingJobId == first.JobId)).Amount);
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var job = await db.ProcessingJobs.SingleAsync(x => x.Id == first.JobId);
                job.Status = ProcessingJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                if (edge.HasValue) { job.ResultObjectKey = "tests/completed.png"; job.ResultWidth = edge.Value; job.ResultHeight = edge.Value / 2; }
                await tokens.CompleteAsync(first.JobId, default);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            if (revisionCost is null)
            {
                await Assert.ThrowsAsync<ConflictException>(() => service.StartAsync(user.Id, project.Id, request, default));
                Assert.Equal(40 - firstCost, await Balance());
                Assert.Equal(1, await db.TokenTransactions.CountAsync(x => x.UserId == user.Id));
            }
            else
            {
                var revision = await service.StartAsync(user.Id, project.Id, request, default);
                Assert.Equal(40 - firstCost - revisionCost.Value, await Balance());
                Assert.Equal(revisionCost.Value, (await db.TokenTransactions.AsNoTracking().SingleAsync(x => x.ProcessingJobId == revision.JobId)).Amount);
                await using (var tx = await db.Database.BeginTransactionAsync())
                {
                    await tokens.RefundAsync(revision.JobId, default);
                    await tokens.RefundAsync(revision.JobId, default);
                    await tx.CommitAsync();
                }
                Assert.Equal(40 - firstCost, await Balance());
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.TokenTransactions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
            await cleanup.ProcessingJobs.Where(x => x.ProjectId == project.Id).ExecuteDeleteAsync();
            await cleanup.LiveLayerProjects.Where(x => x.Id == project.Id).ExecuteDeleteAsync();
            await cleanup.Images.Where(x => x.Id == imageId).ExecuteDeleteAsync();
            await cleanup.UserTokenWallets.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    private sealed class StartOnlyProcessor : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => true;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct) =>
            throw new NotSupportedException("이 검사는 GPU 작업을 실행하지 않습니다.");
    }
}
