using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QianYuan.Api.Configuration;
using QianYuan.Api.Models;
using QianYuan.Data;
using QianYuan.Data.Entities;

namespace QianYuan.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Pbkdf2Iterations = 210_000;

    private readonly QianYuanDbContext _db;
    private readonly AuthOptions _options;
    private readonly ICreditService _credits;

    public AuthService(QianYuanDbContext db, IOptions<AuthOptions> options, ICreditService credits)
    {
        _db = db;
        _options = options.Value;
        _credits = credits;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);

        if (await _db.UserAccounts.AnyAsync(u => u.Email == email, ct))
        {
            throw new InvalidOperationException("Email already registered.");
        }

        var user = new UserAccount
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email.Split('@')[0] : request.DisplayName.Trim(),
            PasswordHash = HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.UserAccounts.Add(user);
        await _db.SaveChangesAsync(ct);
        await _credits.EnsureWalletAsync(user.Id, ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
            !VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (stored.RevokedAt is not null || stored.ExpiresAt <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Refresh token expired or revoked.");
        }

        stored.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(stored.User, ct);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (stored is null) return;
        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(UserAccount user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(5, _options.AccessTokenMinutes));
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
        };

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        var refreshToken = CreateOpaqueToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = now.AddDays(Math.Max(1, _options.RefreshTokenDays)),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, refreshToken, expiresAt, ToDto(user));
    }

    private static AuthUserDto ToDto(UserAccount user) => new(user.Id, user.Email, user.DisplayName, user.Status);

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid email is required.");
        }

        return email.Trim().ToLowerInvariant();
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.");
        }
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2-sha256${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string CreateOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}