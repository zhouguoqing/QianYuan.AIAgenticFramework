namespace QianYuan.Api.Configuration;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "WorkPartner";
    public string Audience { get; set; } = "WorkPartner.Client";
    public string SigningKey { get; set; } = "workpartner-dev-signing-key-change-before-production-2026";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}