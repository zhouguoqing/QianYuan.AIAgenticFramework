namespace QianYuan.Api.Models;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record AuthUserDto(Guid Id, string Email, string DisplayName, string Status);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    AuthUserDto User);

public sealed record AccountUpdateRequest(string? DisplayName);