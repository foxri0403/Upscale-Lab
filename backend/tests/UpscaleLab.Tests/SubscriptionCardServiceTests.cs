using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class SubscriptionCardServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CardChangeChecksOwnershipAndKeyVersionBeforeCallingToss(bool differentAccount)
    {
        await using var db = CreateDb();
        var config = TestConfiguration();
        var protector = new BillingKeyProtector(config);
        using var gateway = new DecliningGateway();
        using var http = new HttpClient(gateway);
        var service = new SubscriptionCardService(db, new TossBillingClient(http, config), protector);
        var subscription = await RegisterAsync(db, protector);
        var change = await service.StartAsync(subscription.UserId, default);

        if (differentAccount)
        {
            var other = await RegisterAsync(db, protector);
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeAsync(other.UserId, change.Challenge, change.CustomerKey, "auth", default));
        }
        else
        {
            subscription.BillingKeyCiphertext = protector.Encrypt("newly-registered-key", change.CustomerKey);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeAsync(subscription.UserId, change.Challenge, change.CustomerKey, "auth", default));
        }

        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task FailedProviderAuthorizationPreservesTheRegisteredCard()
    {
        await using var db = CreateDb();
        var config = TestConfiguration();
        var protector = new BillingKeyProtector(config);
        using var gateway = new DecliningGateway();
        using var http = new HttpClient(gateway);
        var service = new SubscriptionCardService(db, new TossBillingClient(http, config), protector);
        var subscription = await RegisterAsync(db, protector);
        var change = await service.StartAsync(subscription.UserId, default);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            service.ChangeAsync(subscription.UserId, change.Challenge, change.CustomerKey, "auth", default));

        Assert.Equal(1, gateway.Calls);
        var stored = await db.UserSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal("original-test-key", protector.Decrypt(stored.BillingKeyCiphertext!, stored.CustomerKey));
        Assert.Equal("1234", stored.CardNumberSuffix);
        Assert.Equal(SubscriptionPlan.Plus, stored.Plan);
        Assert.Equal(subscription.NextBillingAtUtc, stored.NextBillingAtUtc);
    }

    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ReplacesTheRegisteredKeyAndRejectsReplaysAndChangesDuringRenewal()
    {
        var connection = Environment.GetEnvironmentVariable("UPSCALELAB_TOKEN_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection), "테스트 전용 연결 문자열이 필요합니다.");
        Assert.Equal("upscalelab_token_test", new Npgsql.NpgsqlConnectionStringBuilder(connection).Database);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
        var config = TestConfiguration();
        var protector = new BillingKeyProtector(config);
        var owner = Guid.NewGuid();
        db.Users.Add(new User { Id = owner, Email = $"{owner:N}@example.invalid", Username = $"card-{owner:N}", PasswordHash = "test" });
        var subscription = await RegisterAsync(db, protector, owner);
        db.ChangeTracker.Clear();

        try
        {
            using var gateway = new ApprovingGateway(subscription.CustomerKey);
            using var http = new HttpClient(gateway);
            var service = new SubscriptionCardService(db, new TossBillingClient(http, config), protector);
            var change = await service.StartAsync(owner, default);
            await service.ChangeAsync(owner, change.Challenge, change.CustomerKey, "auth", default);
            var stored = await db.UserSubscriptions.AsNoTracking().SingleAsync(x => x.UserId == owner);
            Assert.Equal("replacement-test-key", protector.Decrypt(stored.BillingKeyCiphertext!, stored.CustomerKey));
            Assert.Equal("51", stored.CardIssuerCode);
            Assert.Equal("9876", stored.CardNumberSuffix);
            Assert.Equal(subscription.NextBillingAtUtc, stored.NextBillingAtUtc);
            await Assert.ThrowsAsync<ConflictException>(() => service.ChangeAsync(owner, change.Challenge, change.CustomerKey, "auth", default));

            db.PaymentOrders.Add(new PaymentOrder { UserId = owner, OrderId = $"UL-{owner:N}", SubscriptionId = subscription.Id,
                BillingPeriodStartUtc = stored.NextBillingAtUtc, ProductCode = "PLUS_MONTHLY", Amount = 4900, Status = PaymentOrderStatus.Confirming });
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<ConflictException>(() => service.StartAsync(owner, default));
            Assert.Equal(1, gateway.Calls);
        }
        finally
        {
            await db.PaymentOrders.Where(x => x.UserId == owner).ExecuteDeleteAsync();
            await db.UserSubscriptions.Where(x => x.UserId == owner).ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id == owner).ExecuteDeleteAsync();
        }
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IConfiguration TestConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["BillingCrypto:KeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["TossPayments:SecretKey"] = "test-placeholder" }).Build();

    private static async Task<UserSubscription> RegisterAsync(ApplicationDbContext db, BillingKeyProtector protector, Guid? owner = null)
    {
        var key = Guid.NewGuid().ToString("N");
        var subscription = new UserSubscription { UserId = owner ?? Guid.NewGuid(), CustomerKey = key,
            Plan = SubscriptionPlan.Plus, Status = SubscriptionStatus.Active, NextBillingAtUtc = DateTime.UtcNow.Date.AddDays(20),
            BillingKeyCiphertext = protector.Encrypt("original-test-key", key), CardNumberSuffix = "1234" };
        db.UserSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    private sealed class ApprovingGateway(string customerKey) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                billingKey = "replacement-test-key", customerKey, card = new { issuerCode = "51", number = "000000******9876" }
            }) });
        }
    }

    private sealed class DecliningGateway : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.InRange(request.Headers.GetValues("Idempotency-Key").Single().Length, 1, 64);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        }
    }
}
