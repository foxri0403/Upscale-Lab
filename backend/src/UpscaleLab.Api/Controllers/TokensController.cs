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
public sealed class TokensController(TokenService tokenService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        var wallet = await tokenService.GetOrRefreshWalletAsync(
            userId, ct);

        var monthlyBalance = wallet.Balance;
        var effectivePeriod = wallet.PeriodStartUtc;

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
