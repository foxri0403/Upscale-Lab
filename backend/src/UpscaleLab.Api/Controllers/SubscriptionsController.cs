using UpscaleLab.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Models;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/subscriptions")]
public sealed class SubscriptionsController(
    ApplicationDbContext db,
    TossBillingClient billing,
    BillingKeyProtector protector,
    SubscriptionChargeClaimService chargeClaim,
    SubscriptionFulfillmentService fulfillment,
    Microsoft.Extensions.Configuration.IConfiguration configuration) : ControllerBase
{

    public sealed record IssueBillingKeyRequest(
        Guid SubscriptionId,
        string AuthKey);

    [HttpPost("billing/issue")]
    public async Task<IActionResult> IssueBillingKey(
        [FromBody] IssueBillingKeyRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (request is null ||
            request.SubscriptionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AuthKey) ||
            request.AuthKey.Length > 300)
            return BadRequest("빌링 인증 정보가 올바르지 않습니다.");

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.SubscriptionId &&
                     x.UserId == userId,
                ct);

        if (subscription is null)
            return NotFound("구독 신청 정보를 찾을 수 없습니다.");

        if (subscription.Status != SubscriptionStatus.Pending ||
            !string.IsNullOrWhiteSpace(subscription.BillingKeyCiphertext))
            return Conflict("빌링키를 등록할 수 없는 구독 상태입니다.");

        if (subscription.Plan is not (
            SubscriptionPlan.Plus or SubscriptionPlan.Pro))
            return Conflict("유효하지 않은 구독 플랜입니다.");

        // 암호화 키가 설정되지 않았다면 외부 발급을 시작하지 않는다.
        // 키 설정 검증을 위해 임시 값을 암호화하고 결과는 폐기한다.
        try
        {
            _ = protector.Encrypt(
                "configuration-check",
                subscription.CustomerKey);
        }
        catch (InvalidOperationException)
        {
            return StatusCode(503, new
            {
                message = "빌링키 암호화 설정이 준비되지 않았습니다."
            });
        }

        TossBillingKeyResult result;

        try
        {
            result = await billing.IssueBillingKeyAsync(
                request.AuthKey,
                subscription.CustomerKey,
                ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new
            {
                message = "빌링키 발급에 실패했습니다. 결제수단 등록 상태를 확인해 주세요."
            });
        }

        var encrypted = protector.Encrypt(
            result.BillingKey,
            subscription.CustomerKey);

        // 동일 구독에 빌링키가 이미 저장됐다면 덮어쓰지 않는다.
        var updated = await db.UserSubscriptions
            .Where(x =>
                x.Id == subscription.Id &&
                x.UserId == userId &&
                x.Status == SubscriptionStatus.Pending &&
                x.BillingKeyCiphertext == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.BillingKeyCiphertext,
                    encrypted)
                .SetProperty(
                    x => x.UpdatedAt,
                    DateTime.UtcNow),
                ct);

        if (updated != 1)
            return Conflict(
                "이미 다른 요청에서 빌링키 등록이 처리되었습니다.");

        return Ok(new
        {
            subscriptionId = subscription.Id,
            plan = subscription.Plan.ToString(),
            status = "Pending",
            billingKeyRegistered = true,
            paymentCompleted = false
        });
    }




    public sealed record InitialChargeRequest(string OrderId);

    [HttpPost("initial-charge")]
    public async Task<IActionResult> InitialCharge(
        [FromBody] InitialChargeRequest request,
        CancellationToken ct)
    {
        // 테스트 검증이 끝날 때까지 실제 청구는 기본 차단한다.
        if (!configuration.GetValue<bool>(
            "Payments:EnableInitialBillingCharge"))
            return StatusCode(503, new
            {
                message = "최초 구독 결제 기능은 아직 비활성화 상태입니다."
            });

        var userId = User.GetRequiredUserId();

        if (request is null ||
            string.IsNullOrWhiteSpace(request.OrderId) ||
            request.OrderId.Length > 64)
            return BadRequest("주문번호가 올바르지 않습니다.");

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrderId == request.OrderId &&
                x.UserId == userId, ct);

        if (order is null || order.SubscriptionId is null ||
            order.BillingPeriodStartUtc is null)
            return NotFound("구독 주문을 찾을 수 없습니다.");

        if (order.Status == PaymentOrderStatus.Paid)
            return Ok(new
            {
                orderId = order.OrderId,
                status = "Paid",
                alreadyProcessed = true
            });

        if (order.Status == PaymentOrderStatus.Confirming)
            return Conflict(new
            {
                message = "결제 진행 상태를 확인해야 합니다. 재청구하지 않습니다.",
                orderId = order.OrderId,
                status = "Confirming"
            });

        if (order.Status != PaymentOrderStatus.Pending)
            return Conflict("청구할 수 없는 주문 상태입니다.");

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == order.SubscriptionId.Value &&
                x.UserId == userId, ct);

        if (subscription is null ||
            subscription.Status != SubscriptionStatus.Pending ||
            string.IsNullOrWhiteSpace(subscription.BillingKeyCiphertext))
            return Conflict("구독 또는 결제수단 상태가 올바르지 않습니다.");

        var productCode = subscription.Plan switch
        {
            SubscriptionPlan.Plus => "PLUS_MONTHLY",
            SubscriptionPlan.Pro => "PRO_MONTHLY",
            _ => ""
        };

        if (productCode.Length == 0 ||
            productCode != order.ProductCode)
            return Conflict("구독 상품이 일치하지 않습니다.");

        var product = PaymentCatalog.Get(productCode);

        if (product.Price != order.Amount || !product.IsSubscription)
            return Conflict("청구 금액이 일치하지 않습니다.");

        string billingKey;

        try
        {
            billingKey = protector.Decrypt(
                subscription.BillingKeyCiphertext,
                subscription.CustomerKey);
        }
        catch (Exception ex) when (
            ex is System.Security.Cryptography.CryptographicException
            or FormatException
            or InvalidOperationException)
        {
            return StatusCode(503, new
            {
                message = "빌링키 복호화에 실패했습니다."
            });
        }

        // 하나의 요청만 Pending -> Confirming으로 변경할 수 있다.
        var claimed = await chargeClaim.TryClaimInitialChargeAsync(
            userId, order.OrderId, ct);

        if (!claimed)
            return Conflict(
                "다른 요청에서 처리 중이거나 완료된 주문입니다.");

        // 이후 장애가 발생해도 임의로 Pending으로 되돌리지 않는다.
        // 결제 결과가 불확실할 수 있기 때문이다.
        TossBillingPaymentResult payment;

        try
        {
            payment = await billing.ChargeAsync(
                billingKey,
                subscription.CustomerKey,
                order.OrderId,
                product.Name,
                order.Amount,
                ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException)
        {
            return StatusCode(503, new
            {
                orderId = order.OrderId,
                status = "Confirming",
                message = "결제 결과 확인이 필요합니다. 다시 청구하지 마세요."
            });
        }

        if (payment.OrderId != order.OrderId ||
            payment.TotalAmount != order.Amount ||
            payment.Status != "DONE" ||
            string.IsNullOrWhiteSpace(payment.PaymentKey))
            return Conflict(
                "결제 응답이 주문 정보와 일치하지 않습니다.");

        var activated =
            await fulfillment.ActivateInitialSubscriptionAsync(
                userId, payment, ct);

        return Ok(new
        {
            orderId = order.OrderId,
            status = "Paid",
            plan = subscription.Plan.ToString(),
            monthlyTokens = product.Tokens,
            alreadyProcessed = !activated
        });
    }


    [HttpPost("orders/{orderId}/recover")]
    public async Task<IActionResult> RecoverInitialCharge(
        string orderId,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64)
            return BadRequest("주문번호가 올바르지 않습니다.");

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrderId == orderId &&
                x.UserId == userId, ct);

        if (order is null || order.SubscriptionId is null)
            return NotFound("구독 결제 주문을 찾을 수 없습니다.");

        if (order.Status == PaymentOrderStatus.Paid)
            return Ok(new
            {
                orderId,
                status = "Paid",
                alreadyProcessed = true
            });

        if (order.Status != PaymentOrderStatus.Confirming ||
            order.BillingPeriodStartUtc is null)
            return Conflict("복구 대상 주문이 아닙니다.");

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == order.SubscriptionId.Value &&
                x.UserId == userId, ct);

        if (subscription is null)
            return NotFound("구독 정보를 찾을 수 없습니다.");

        if (subscription.Status != SubscriptionStatus.Pending)
            return Conflict("최초 결제 구독 상태가 올바르지 않습니다.");

        var productCode = subscription.Plan switch
        {
            SubscriptionPlan.Plus => "PLUS_MONTHLY",
            SubscriptionPlan.Pro => "PRO_MONTHLY",
            _ => ""
        };

        if (productCode.Length == 0 ||
            order.ProductCode != productCode)
            return Conflict("구독 상품 정보가 일치하지 않습니다.");

        var product = PaymentCatalog.Get(productCode);

        if (product.Price != order.Amount)
            return Conflict("구독 결제 금액이 일치하지 않습니다.");

        TossBillingPaymentResult payment;

        try
        {
            payment = await billing.GetPaymentByOrderIdAsync(
                order.OrderId, ct);
        }
        catch (HttpRequestException)
        {
            // 조회 실패는 미결제의 증거가 아니다.
            // 주문은 Confirming으로 유지하며 재청구하지 않는다.
            return StatusCode(503, new
            {
                orderId,
                status = "Confirming",
                message = "결제 상태를 확인하지 못했습니다. 재청구하지 않습니다."
            });
        }

        if (payment.OrderId != order.OrderId ||
            payment.TotalAmount != order.Amount ||
            string.IsNullOrWhiteSpace(payment.PaymentKey))
            return Conflict("토스페이먼츠 결제 정보가 일치하지 않습니다.");

        if (payment.Status != "DONE")
            return Conflict(new
            {
                orderId,
                paymentStatus = payment.Status,
                message = "완료된 결제가 아니므로 구독을 활성화하지 않습니다."
            });

        var activated =
            await fulfillment.ActivateInitialSubscriptionAsync(
                userId, payment, ct);

        return Ok(new
        {
            orderId,
            status = "Paid",
            plan = subscription.Plan.ToString(),
            monthlyTokens = product.Tokens,
            alreadyProcessed = !activated
        });
    }

    public sealed record InitialChargePreflightRequest(string OrderId);

    [HttpPost("initial-charge/preflight")]
    public async Task<IActionResult> InitialChargePreflight(
        [FromBody] InitialChargePreflightRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (request is null ||
            string.IsNullOrWhiteSpace(request.OrderId) ||
            request.OrderId.Length > 64)
            return BadRequest("주문번호가 올바르지 않습니다.");

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrderId == request.OrderId &&
                x.UserId == userId, ct);

        if (order is null || order.SubscriptionId is null)
            return NotFound("구독 결제 주문을 찾을 수 없습니다.");

        if (order.Status != PaymentOrderStatus.Pending)
            return Conflict(
                "이미 결제 진행 중이거나 완료된 주문입니다.");

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == order.SubscriptionId.Value &&
                x.UserId == userId, ct);

        if (subscription is null ||
            subscription.Status != SubscriptionStatus.Pending ||
            string.IsNullOrWhiteSpace(
                subscription.BillingKeyCiphertext))
            return Conflict("구독 또는 결제수단 상태가 올바르지 않습니다.");

        var productCode = subscription.Plan switch
        {
            SubscriptionPlan.Plus => "PLUS_MONTHLY",
            SubscriptionPlan.Pro => "PRO_MONTHLY",
            _ => ""
        };

        if (productCode.Length == 0 ||
            order.ProductCode != productCode)
            return Conflict("구독 상품이 일치하지 않습니다.");

        var product = PaymentCatalog.Get(productCode);

        if (order.Amount != product.Price ||
            order.BillingPeriodStartUtc is null)
            return Conflict("주문 금액 또는 청구 기간이 올바르지 않습니다.");

        try
        {
            var billingKey = protector.Decrypt(
                subscription.BillingKeyCiphertext,
                subscription.CustomerKey);

            if (string.IsNullOrWhiteSpace(billingKey))
                return Conflict("결제수단 정보가 올바르지 않습니다.");
        }
        catch (Exception ex) when (
            ex is System.Security.Cryptography.CryptographicException
            or FormatException
            or InvalidOperationException)
        {
            return StatusCode(503, new
            {
                message = "빌링키 복호화에 실패했습니다. 암호화 설정을 확인해 주세요."
            });
        }

        return Ok(new
        {
            orderId = order.OrderId,
            subscriptionId = subscription.Id,
            plan = subscription.Plan.ToString(),
            amount = order.Amount,
            status = "Ready",
            paymentExecuted = false
        });
    }

    public sealed record InitialOrderRequest(
        Guid SubscriptionId);

    [HttpPost("initial-order")]
    public async Task<IActionResult> CreateInitialOrder(
        [FromBody] InitialOrderRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (request.SubscriptionId == Guid.Empty)
            return BadRequest("구독 ID가 올바르지 않습니다.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(ct);

            // 동일 구독의 동시 주문 생성을 직렬화한다.
            var subscription = await db.UserSubscriptions
                .FromSqlInterpolated($"""
                    SELECT * FROM user_subscriptions
                    WHERE "Id" = {request.SubscriptionId}
                      AND "UserId" = {userId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (subscription is null)
                return (IActionResult)NotFound("구독 신청 정보를 찾을 수 없습니다.");

            if (subscription.Status != SubscriptionStatus.Pending)
                return (IActionResult)Conflict("최초 결제 가능한 구독 상태가 아닙니다.");

            if (string.IsNullOrWhiteSpace(
                subscription.BillingKeyCiphertext))
                return (IActionResult)Conflict("결제수단 등록을 먼저 완료해 주세요.");

            string productCode = subscription.Plan switch
            {
                SubscriptionPlan.Plus => "PLUS_MONTHLY",
                SubscriptionPlan.Pro => "PRO_MONTHLY",
                _ => ""
            };

            if (productCode.Length == 0)
                return (IActionResult)Conflict("구독 플랜이 올바르지 않습니다.");

            var product = PaymentCatalog.Get(productCode);

            // 최초 구독 결제 주문은 한 건만 생성한다.
            var existing = await db.PaymentOrders
                .AsNoTracking()
                .Where(x =>
                    x.SubscriptionId == subscription.Id &&
                    x.Status != PaymentOrderStatus.Canceled &&
                    x.Status != PaymentOrderStatus.Failed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                if (existing.Status is PaymentOrderStatus.Canceled
                    or PaymentOrderStatus.Refunded
                    or PaymentOrderStatus.Failed)
                    return (IActionResult)Conflict(
                        "기존 결제 주문 확인이 필요합니다.");

                await transaction.CommitAsync(ct);

                return (IActionResult)Ok(new
                {
                    orderId = existing.OrderId,
                    productCode = existing.ProductCode,
                    amount = existing.Amount,
                    status = existing.Status.ToString(),
                    alreadyCreated = true
                });
            }

            var now = DateTime.UtcNow;

            var order = new PaymentOrder
            {
                UserId = userId,
                OrderId = "UL-SUB-" + Guid.NewGuid().ToString("N"),
                SubscriptionId = subscription.Id,
                BillingPeriodStartUtc = now,
                ProductCode = product.Code,
                Amount = product.Price,
                Status = PaymentOrderStatus.Pending
            };

            db.PaymentOrders.Add(order);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return (IActionResult)Ok(new
            {
                orderId = order.OrderId,
                productCode = order.ProductCode,
                amount = order.Amount,
                status = "Pending",
                alreadyCreated = false
            });
        });
    }


    [HttpPost("cancel")]
    public async Task<IActionResult> CancelSubscription(
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx =
                await db.Database.BeginTransactionAsync(ct);

            // 청구 선점과 같은 구독 행 잠금 사용
            var subscription = await db.UserSubscriptions
                .FromSqlInterpolated($"""
                    SELECT * FROM user_subscriptions
                    WHERE "UserId" = {userId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (subscription is null)
                return (IActionResult)NotFound("구독 정보를 찾을 수 없습니다.");

            if (subscription.Status != SubscriptionStatus.Active)
                return (IActionResult)Conflict("해지 가능한 활성 구독이 없습니다.");

            if (subscription.CancelAtPeriodEnd)
            {
                await tx.CommitAsync(ct);

                return (IActionResult)Ok(new
                {
                    status = "CancellationScheduled",
                    cancelAtPeriodEnd = true,
                    currentPeriodEndUtc =
                        subscription.CurrentPeriodEndUtc,
                    alreadyScheduled = true
                });
            }

            // 이미 청구 선점된 주문이 있으면
            // 결제 결과를 확인하기 전까지 해지를 확정하지 않는다.
            var confirming = await db.PaymentOrders.AnyAsync(
                o => o.SubscriptionId == subscription.Id &&
                     o.Status == PaymentOrderStatus.Confirming &&
                     o.BillingPeriodStartUtc ==
                         subscription.NextBillingAtUtc,
                ct);

            if (confirming)
            {
                return (IActionResult)Conflict(new
                {
                    code = "RENEWAL_PAYMENT_IN_PROGRESS",
                    message = "구독 결제 확인이 진행 중입니다. 결제 결과 확인 후 다시 해지해 주세요."
                });
            }

            subscription.CancelAtPeriodEnd = true;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (IActionResult)Ok(new
            {
                status = "CancellationScheduled",
                cancelAtPeriodEnd = true,
                currentPeriodEndUtc =
                    subscription.CurrentPeriodEndUtc,
                alreadyScheduled = false
            });
        });
    }


    [HttpPost("pending/cancel")]
    public async Task<IActionResult> CancelPendingSubscription(
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx =
                await db.Database.BeginTransactionAsync(ct);

            var subscription = await db.UserSubscriptions
                .FromSqlInterpolated($"""
                    SELECT * FROM user_subscriptions
                    WHERE "UserId" = {userId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (subscription is null)
                return (IActionResult)NotFound(
                    "구독 신청 정보가 없습니다.");

            if (subscription.Status != SubscriptionStatus.Pending)
                return (IActionResult)Conflict(
                    "대기 중인 구독만 취소할 수 있습니다.");

            var orders = await db.PaymentOrders
                .Where(x => x.SubscriptionId == subscription.Id)
                .ToListAsync(ct);

            if (orders.Any(x =>
                x.Status != PaymentOrderStatus.Pending &&
                x.Status != PaymentOrderStatus.Failed &&
                x.Status != PaymentOrderStatus.Canceled))
            {
                return (IActionResult)Conflict(
                    "결제가 진행 중이거나 처리된 구독은 취소할 수 없습니다.");
            }

            foreach (var order in orders.Where(x =>
                x.Status == PaymentOrderStatus.Pending))
                order.Status = PaymentOrderStatus.Canceled;

            subscription.Status = SubscriptionStatus.Canceled;
            subscription.BillingKeyCiphertext = null;
            subscription.CancelAtPeriodEnd = false;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (IActionResult)Ok(new
            {
                status = "Canceled",
                canceledOrders = orders.Count
            });
        });
    }


    [HttpGet("me")]
    public async Task<IActionResult> GetMySubscription(
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);

        if (subscription is null)
            return Ok(new { status = "None" });

        var order = await db.PaymentOrders
            .AsNoTracking()
            .Where(x =>
                x.SubscriptionId == subscription.Id &&
                x.Status != PaymentOrderStatus.Canceled &&
                x.Status != PaymentOrderStatus.Failed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            subscriptionId = subscription.Id,
            plan = subscription.Plan.ToString(),
            status = subscription.Status.ToString(),
            billingKeyRegistered =
                !string.IsNullOrWhiteSpace(
                    subscription.BillingKeyCiphertext),
            cancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            currentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            orderId = order?.OrderId,
            orderStatus = order?.Status.ToString()
        });
    }

    public sealed record StartSubscriptionRequest(
        string ProductCode);

    [HttpPost("start")]
    public async Task<IActionResult> Start(
        [FromBody] StartSubscriptionRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (request is null ||
            string.IsNullOrWhiteSpace(request.ProductCode) ||
            request.ProductCode is not ("PLUS_MONTHLY" or "PRO_MONTHLY"))
        {
            return BadRequest("Plus 또는 Pro 플랜을 선택해 주세요.");
        }

        var product = PaymentCatalog.Get(request.ProductCode);

        var plan = request.ProductCode == "PLUS_MONTHLY"
            ? SubscriptionPlan.Plus
            : SubscriptionPlan.Pro;

        var existing = await db.UserSubscriptions
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);

        if (existing is not null)
        {
            if (existing.Status == SubscriptionStatus.Pending &&
                existing.Plan == plan &&
                string.IsNullOrWhiteSpace(existing.BillingKeyCiphertext))
            {
                return Ok(new
                {
                    subscriptionId = existing.Id,
                    plan = existing.Plan.ToString(),
                    customerKey = existing.CustomerKey,
                    amount = product.Price,
                    status = "Pending",
                    alreadyRequested = true
                });
            }

            if (existing.Status == SubscriptionStatus.Canceled &&
                existing.CurrentPeriodStartUtc == null &&
                existing.CurrentPeriodEndUtc == null &&
                existing.NextBillingAtUtc == null &&
                string.IsNullOrWhiteSpace(existing.BillingKeyCiphertext))
            {
                var hasProcessedOrders = await db.PaymentOrders
                    .AnyAsync(x =>
                        x.SubscriptionId == existing.Id &&
                        x.Status != PaymentOrderStatus.Canceled &&
                        x.Status != PaymentOrderStatus.Failed, ct);

                if (hasProcessedOrders)
                    return Conflict(
                        "기존 결제 주문 확인이 필요합니다.");

                existing.Plan = plan;
                existing.Status = SubscriptionStatus.Pending;
                existing.CustomerKey =
                    "ULC-" + Guid.NewGuid().ToString("N");
                existing.BillingKeyCiphertext = null;
                existing.CancelAtPeriodEnd = false;

                await db.SaveChangesAsync(ct);

                return Ok(new
                {
                    subscriptionId = existing.Id,
                    plan = existing.Plan.ToString(),
                    customerKey = existing.CustomerKey,
                    amount = product.Price,
                    status = "Pending",
                    alreadyRequested = false
                });
            }

            return Conflict(
                "기존 구독 또는 처리 중인 신청이 있습니다. 구독 변경 기능에서 처리해야 합니다.");
        }

        var subscription = new UserSubscription
        {
            UserId = userId,
            Plan = plan,
            Status = SubscriptionStatus.Pending,
            CustomerKey = "ULC-" + Guid.NewGuid().ToString("N"),
            BillingKeyCiphertext = null,
            CancelAtPeriodEnd = false
        };

        db.UserSubscriptions.Add(subscription);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg &&
                  pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // 동시에 신청한 경우 중복 생성 방지
            return Conflict("이미 구독 신청이 존재합니다.");
        }

        return Ok(new
        {
            subscriptionId = subscription.Id,
            plan = subscription.Plan.ToString(),
            customerKey = subscription.CustomerKey,
            amount = product.Price,
            status = "Pending",
            alreadyRequested = false
        });
    }
}
