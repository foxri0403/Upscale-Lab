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
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(
    ApplicationDbContext db,
    TossPaymentsClient toss,
    PaymentFulfillmentService fulfillment) : ControllerBase
{
    [HttpGet("products")]
    public IActionResult GetProducts()
    {
        return Ok(PaymentCatalog.All);
    }

    public sealed record CreateOrderRequest(string ProductCode);

    [Authorize]
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (string.IsNullOrWhiteSpace(request.ProductCode))
            return BadRequest("상품 코드를 선택해 주세요.");

        PaymentProduct product;

        try
        {
            product = PaymentCatalog.Get(request.ProductCode);
        }
        catch (ArgumentException)
        {
            return BadRequest("존재하지 않는 상품입니다.");
        }

        // 정기구독은 빌링키 결제 기능 완성 후 별도로 처리
        if (product.IsSubscription)
            return BadRequest(
                "구독 결제는 정기결제 기능 연결 후 이용할 수 있습니다.");

        var order = new PaymentOrder
        {
            UserId = userId,
            OrderId = $"UL-{Guid.NewGuid():N}",
            ProductCode = product.Code,
            Amount = product.Price,
            Status = PaymentOrderStatus.Pending
        };

        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            orderId = order.OrderId,
            productCode = order.ProductCode,
            orderName = product.Name,
            amount = order.Amount,
            status = order.Status.ToString()
        });
    }


    public sealed record ConfirmPaymentRequest(
        string OrderId,
        string PaymentKey,
        int Amount);

    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmPayment(
        [FromBody] ConfirmPaymentRequest request,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (string.IsNullOrWhiteSpace(request.OrderId) ||
            string.IsNullOrWhiteSpace(request.PaymentKey) ||
            request.OrderId.Length > 64 ||
            request.PaymentKey.Length > 200 ||
            request.Amount <= 0)
            return BadRequest("결제 정보가 올바르지 않습니다.");

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == request.OrderId &&
                     x.UserId == userId, ct);

        if (order is null)
            return NotFound("주문을 찾을 수 없습니다.");

        if (order.Amount != request.Amount)
            return BadRequest("결제 금액이 일치하지 않습니다.");

        if (order.Status == PaymentOrderStatus.Paid)
        {
            if (order.PaymentKey != request.PaymentKey)
                return Conflict("다른 결제 키로 완료된 주문입니다.");

            return Ok(new
            {
                orderId = order.OrderId,
                status = "Paid",
                alreadyProcessed = true
            });
        }

        if (order.Status is not (
            PaymentOrderStatus.Pending or
            PaymentOrderStatus.Confirming))
            return Conflict("승인할 수 없는 주문 상태입니다.");

        var product = PaymentCatalog.Get(order.ProductCode);

        if (product.IsSubscription ||
            product.Price != order.Amount)
            return BadRequest("일반결제로 승인할 수 없는 상품입니다.");

        // 외부 결제 승인 전에 paymentKey를 저장해 복구 가능하게 한다.
        // 다른 결제 키가 이미 연결된 주문은 재승인을 차단한다.
        var claimed = await db.PaymentOrders
            .Where(x => x.Id == order.Id &&
                (x.Status == PaymentOrderStatus.Pending ||
                 x.Status == PaymentOrderStatus.Confirming) &&
                (x.PaymentKey == null ||
                 x.PaymentKey == request.PaymentKey))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.PaymentKey, request.PaymentKey)
                .SetProperty(x => x.Status, PaymentOrderStatus.Confirming)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow),
                ct);

        if (claimed != 1)
            return Conflict(
                "이미 다른 결제로 처리 중이거나 완료된 주문입니다.");

        TossPaymentResult verified;

        try
        {
            verified = await toss.ConfirmAsync(
                request.PaymentKey,
                order.OrderId,
                order.Amount,
                ct);
        }
        catch (HttpRequestException)
        {
            // 승인 응답이 유실됐을 가능성이 있으므로 상태 조회
            try
            {
                verified = await toss.GetPaymentAsync(
                    request.PaymentKey, ct);
            }
            catch (HttpRequestException)
            {
                return StatusCode(503, new
                {
                    message = "결제 상태를 확인하지 못했습니다. 주문 상태를 다시 조회해 주세요."
                });
            }
        }

        if (verified.PaymentKey != request.PaymentKey ||
            verified.OrderId != order.OrderId ||
            verified.TotalAmount != order.Amount ||
            verified.Status != "DONE")
            return Conflict(
                "토스페이먼츠 결제가 완료되지 않았거나 주문 정보가 다릅니다.");

        var granted = await fulfillment.FulfillTokenPurchaseAsync(
            userId, verified, ct);

        return Ok(new
        {
            orderId = order.OrderId,
            status = "Paid",
            tokens = product.Tokens,
            alreadyProcessed = !granted
        });
    }


    [Authorize]
    [HttpPost("orders/{orderId}/recover")]
    public async Task<IActionResult> RecoverPayment(
        string orderId,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        if (string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64)
            return BadRequest("주문번호가 올바르지 않습니다.");

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId &&
                     x.UserId == userId,
                ct);

        if (order is null)
            return NotFound("주문을 찾을 수 없습니다.");

        if (order.Status == PaymentOrderStatus.Paid)
            return Ok(new
            {
                orderId,
                status = "Paid",
                alreadyProcessed = true
            });

        if (order.Status != PaymentOrderStatus.Confirming ||
            string.IsNullOrWhiteSpace(order.PaymentKey))
            return Conflict(
                "복구할 결제 승인 요청이 없습니다.");

        var product = PaymentCatalog.Get(order.ProductCode);

        if (product.IsSubscription ||
            product.Price != order.Amount)
            return Conflict(
                "복구할 수 없는 결제 상품입니다.");

        TossPaymentResult payment;

        try
        {
            payment = await toss.GetPaymentAsync(
                order.PaymentKey, ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new
            {
                message = "결제 상태를 확인할 수 없습니다. 나중에 다시 시도해 주세요."
            });
        }

        if (payment.PaymentKey != order.PaymentKey ||
            payment.OrderId != order.OrderId ||
            payment.TotalAmount != order.Amount)
            return Conflict(
                "결제 정보가 저장된 주문과 일치하지 않습니다.");

        if (payment.Status != "DONE")
            return Conflict(new
            {
                message = "아직 완료된 결제가 아닙니다.",
                paymentStatus = payment.Status
            });

        var granted = await fulfillment.FulfillTokenPurchaseAsync(
            userId, payment, ct);

        return Ok(new
        {
            orderId,
            status = "Paid",
            tokens = product.Tokens,
            alreadyProcessed = !granted
        });
    }

    [Authorize]
    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> GetOrder(
        string orderId,
        CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var order = await db.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId &&
                     x.UserId == userId,
                ct);

        if (order is null)
            return NotFound("주문을 찾을 수 없습니다.");

        return Ok(new
        {
            orderId = order.OrderId,
            productCode = order.ProductCode,
            amount = order.Amount,
            status = order.Status.ToString(),
            createdAt = order.CreatedAt,
            paidAt = order.PaidAt
        });
    }
}
