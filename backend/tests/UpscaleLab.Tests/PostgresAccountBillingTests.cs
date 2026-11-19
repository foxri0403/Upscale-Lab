using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresAccountBillingTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PartialRefundReservesOnlyUnusedTokensAndRecoversWithoutAnotherCancellation()
    {
        var connection = Environment.GetEnvironmentVariable("UPSCALELAB_TOKEN_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection), "테스트 전용 연결 문자열이 필요합니다.");
        Assert.Equal("upscalelab_token_test", new Npgsql.NpgsqlConnectionStringBuilder(connection).Database);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var id = Guid.NewGuid();
        var user = new User { Id = id, Email = $"{id:N}@example.invalid", Username = $"refund-{id:N}", PasswordHash = "test" };
        var old = new PaymentOrder
        {
            UserId = id, OrderId = $"UL-{id:N}", ProductCode = "TOKEN_10", Amount = 1900, Status = PaymentOrderStatus.Paid,
            PaymentKey = $"payment-{id:N}", PaidAt = DateTime.UtcNow.AddDays(-1)
        };
        var recent = new PaymentOrder
        {
            UserId = id, OrderId = $"UL-{Guid.NewGuid():N}", ProductCode = "TOKEN_30", Amount = 4900,
            Status = PaymentOrderStatus.Paid, PaymentKey = $"payment-{Guid.NewGuid():N}", PaidAt = DateTime.UtcNow
        };
        db.AddRange(user, old, recent, new UserTokenWallet { UserId = id, PurchasedBalance = 35 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var gateway = new FakeToss(old.OrderId, old.PaymentKey!);
        using var http = new HttpClient(gateway);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["TossPayments:SecretKey"] = "test-placeholder" }).Build();
        var service = new AccountBillingService(db, new TossPaymentsClient(http, config));

        try
        {
            var ct = CancellationToken.None;
            var summary = await service.GetAsync(id, ct);
            var quote = Assert.Single(summary.Payments, payment => payment.OrderId == old.OrderId);
            Assert.Equal(5, quote.RefundableTokens);
            Assert.Equal(950, quote.RefundableAmount);
            Assert.Equal(30, summary.Payments.Single(payment => payment.OrderId == recent.OrderId).RefundableTokens);
            await Assert.ThrowsAsync<NotFoundException>(() => service.RefundAsync(Guid.NewGuid(), old.OrderId, 5, 950, ct));
            await Assert.ThrowsAsync<ConflictException>(() => service.RefundAsync(id, old.OrderId, 10, 1900, ct));

            Assert.Equal(PaymentOrderStatus.Refunding, await service.RefundAsync(id, old.OrderId, 5, 950, ct));
            Assert.Equal(PaymentOrderStatus.Refunding, await service.RefundAsync(id, old.OrderId, 5, 950, ct));
            Assert.Equal(30, await db.UserTokenWallets.Where(wallet => wallet.UserId == id).Select(wallet => wallet.PurchasedBalance).SingleAsync());

            var pending = await db.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == old.Id);
            Assert.Equal(PaymentOrderStatus.Refunded, await service.CompleteRefundAsync(pending, ct));
            Assert.Equal(PaymentOrderStatus.Refunded, await service.RefundAsync(id, old.OrderId, 5, 950, ct));
            Assert.Equal(1, gateway.CancelCalls);
            summary = await service.GetAsync(id, ct);
            Assert.Equal(30, summary.PurchasedBalance);
            var refund = Assert.Single(summary.Payments, payment => payment.OrderId == old.OrderId);
            Assert.Equal(950, refund.RefundAmount);
            Assert.Equal(5, refund.RefundTokens);
            Assert.NotNull(refund.RefundedAt);
        }
        finally
        {
            await db.PaymentOrders.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await db.UserTokenWallets.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id == id).ExecuteDeleteAsync();
        }
    }

    private sealed class FakeToss(string orderId, string key) : HttpMessageHandler
    {
        public int CancelCalls { get; private set; }
        private bool loseLookup;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.Contains(key)) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post)
            {
                CancelCalls++;
                Assert.Equal($"upscalelab-refund-{orderId}", request.Headers.GetValues("Idempotency-Key").Single());
                var body = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("\"cancelAmount\":950", body);
                loseLookup = true;
                throw new HttpRequestException("Lost cancellation response");
            }

            if (loseLookup)
            {
                loseLookup = false;
                throw new HttpRequestException("Temporary lookup failure");
            }
            var payment = CancelCalls == 0
                ? new { paymentKey = key, orderId, totalAmount = 1900, status = "DONE", cancels = Array.Empty<object>() }
                : new { paymentKey = key, orderId, totalAmount = 1900, status = "PARTIAL_CANCELED",
                    cancels = new object[] { new { cancelAmount = 950, cancelStatus = "DONE", canceledAt = DateTimeOffset.UtcNow,
                        cancelReason = $"Upscale Lab 추가 구매 토큰 환불 {orderId}" } } };
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json")
            };
        }
    }
}
