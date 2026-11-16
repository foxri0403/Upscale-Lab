using System.Net;
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

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IConfiguration TestConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["BillingCrypto:KeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["TossPayments:SecretKey"] = "test-placeholder" }).Build();

    private static async Task<UserSubscription> RegisterAsync(ApplicationDbContext db, BillingKeyProtector protector)
    {
        var key = Guid.NewGuid().ToString("N");
        var subscription = new UserSubscription { UserId = Guid.NewGuid(), CustomerKey = key,
            Plan = SubscriptionPlan.Plus, Status = SubscriptionStatus.Active, NextBillingAtUtc = DateTime.UtcNow.AddDays(20),
            BillingKeyCiphertext = protector.Encrypt("original-test-key", key), CardNumberSuffix = "1234" };
        db.UserSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
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
