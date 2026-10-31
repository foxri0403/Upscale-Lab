using Microsoft.AspNetCore.Mvc.ModelBinding;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Models;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Tokens;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tokens")]
public sealed class TokensController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var wallet = await db.UserTokenWallets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);

        if (wallet == null)
        {
            return Ok(new
            {
                plan = "Free",
                monthlyBalance = 10,
                purchasedBalance = 0,
                totalBalance = 10,
                monthlyAllowance = 10,
                periodStartUtc = TokenService.CurrentPeriodUtc()
            });
        }

        var currentPeriod = TokenService.CurrentPeriodUtc();

        var freeRenewal =
            wallet.Plan == SubscriptionPlan.Free &&
            wallet.PeriodStartUtc < currentPeriod;

        var monthlyBalance = freeRenewal
            ? TokenPolicy.MonthlyAllowance(SubscriptionPlan.Free)
            : wallet.Balance;

        var effectivePeriod = freeRenewal
            ? currentPeriod
            : wallet.PeriodStartUtc;

        return Ok(new
        {
            plan = wallet.Plan.ToString(),
            monthlyBalance,
            purchasedBalance = wallet.PurchasedBalance,
            totalBalance = monthlyBalance + wallet.PurchasedBalance,
            monthlyAllowance = TokenPolicy.MonthlyAllowance(wallet.Plan),
            periodStartUtc = effectivePeriod
        });
    }
}
