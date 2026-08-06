using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

[ApiController]
[Authorize]
public sealed class CreditsController : ControllerBase
{
    private readonly ICreditService _credits;

    public CreditsController(ICreditService credits)
    {
        _credits = credits;
    }

    [HttpGet("api/credits/wallet")]
    public async Task<ActionResult<CreditWalletDto>> Wallet(CancellationToken ct)
    {
        return Ok(await _credits.EnsureWalletAsync(GetUserId(), ct));
    }

    [HttpGet("api/credits/transactions")]
    public async Task<ActionResult<IReadOnlyList<CreditTransactionDto>>> Transactions([FromQuery] int take = 30, CancellationToken ct = default)
    {
        return Ok(await _credits.ListTransactionsAsync(GetUserId(), take, ct));
    }

    [HttpPost("api/credits/estimate")]
    public ActionResult<EstimateCreditsResponse> Estimate(EstimateCreditsRequest request)
    {
        return Ok(_credits.Estimate(request));
    }

    [HttpPost("api/credits/consume")]
    public async Task<ActionResult<CreditTransactionDto>> Consume(ConsumeCreditsRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _credits.ConsumeAsync(GetUserId(), request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, ex.Message);
        }
    }

    [HttpGet("api/plans")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> Plans(CancellationToken ct)
    {
        return Ok(await _credits.ListPlansAsync(ct));
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}