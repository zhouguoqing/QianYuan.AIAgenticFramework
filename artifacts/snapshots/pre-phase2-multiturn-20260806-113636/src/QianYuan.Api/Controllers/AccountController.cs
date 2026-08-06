using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Data;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController : ControllerBase
{
    private readonly QianYuanDbContext _db;

    public AccountController(QianYuanDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();
        return Ok(new AuthUserDto(user.Id, user.Email, user.DisplayName, user.Status));
    }

    [HttpPatch("me")]
    public async Task<ActionResult<AuthUserDto>> Update(AccountUpdateRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new AuthUserDto(user.Id, user.Email, user.DisplayName, user.Status));
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}