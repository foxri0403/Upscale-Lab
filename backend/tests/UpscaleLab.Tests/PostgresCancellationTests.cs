using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresCancellationTests
{
    [Theory]
    [Trait("Category", "PostgreSQLIntegration")]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(2, 10)]
    public async Task ConcurrentCancellation_ConsumesReservationOnceWithoutRefund(int monthly, int purchased)
    {
        var connection = Environment.GetEnvironmentVariable("UPSCALELAB_TOKEN_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        Assert.Equal("upscalelab_token_test", new Npgsql.NpgsqlConnectionStringBuilder(connection).Database);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var user = new User { Email = $"cancel-{Guid.NewGuid():N}@example.invalid",
            Username = "cancel-" + Guid.NewGuid().ToString("N")[..12], PasswordHash = "test-only" };
        var project = WorkflowData.Project(user.Id); var imageId = project.OriginalImage.Id;
        db.Add(user); db.Add(project);
        db.UserTokenWallets.Add(new UserTokenWallet { UserId = user.Id, Plan = SubscriptionPlan.Free,
            Balance = monthly, PurchasedBalance = purchased, PeriodStartUtc = TokenService.CurrentPeriodUtc() });
        await db.SaveChangesAsync();
        // EF omits an inserted zero when the column has a database default of 10.
        // Seed a genuinely exhausted monthly wallet before testing purchased tokens.
        await db.UserTokenWallets.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Balance, monthly));
        static ProjectProcessingService Service(ApplicationDbContext context) => new(context, new WorkflowStore(),
            new NoGpuProcessor(), new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance, new TokenService(context));
        try
        {
            var service = Service(db);
            var job = await service.StartAsync(user.Id, project.Id, new(ProcessingOperation.CharacterLayers), default);
            await using var other = new ApplicationDbContext(options);
            await Task.WhenAll(service.CancelAsync(user.Id, project.Id, job.JobId, default),
                Service(other).CancelAsync(user.Id, project.Id, job.JobId, default));
            // A late failure/refund request must not return a user-cancelled reservation.
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await new TokenService(db).RefundAsync(job.JobId, default);
                await tx.CommitAsync();
            }
            var wallet = await db.UserTokenWallets.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            Assert.Equal(Math.Max(0, monthly - 7), wallet.Balance);
            Assert.Equal(purchased - Math.Max(0, 7 - monthly), wallet.PurchasedBalance);
            var ledger = await db.TokenTransactions.AsNoTracking().SingleAsync(x => x.ProcessingJobId == job.JobId);
            Assert.Equal(TokenTransactionStatus.Completed, ledger.Status); Assert.Equal(7, ledger.Amount);
            Assert.Equal(ProcessingJobStatus.Cancelled, (await service.GetStatusAsync(user.Id, project.Id, default)).Status);
            await service.ExecuteAsync(job.JobId, default);
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

    private sealed class NoGpuProcessor : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => true;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Cancelled jobs must not invoke a GPU.");
    }
}
