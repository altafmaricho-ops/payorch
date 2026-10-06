namespace PayOrch.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "PayOrchestrator";
    public string Audience { get; set; } = "PayOrchestrator.Admin";

    /// <summary>Symmetric signing key. Must be at least 32 bytes for HS256.
    /// In production, load this from a secrets vault, never appsettings.json.</summary>
    public string SigningKey { get; set; } = default!;

    public int ExpiryMinutes { get; set; } = 60;
}
