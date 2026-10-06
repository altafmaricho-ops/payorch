namespace PayOrch.Domain.Entities;

public class PaymentProvider
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? TenantId { get; set; }
    public string ProviderCode { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string AccountLabel { get; set; } = default!;
    public string? MerchantAccountRef { get; set; }
    public string? KeyId { get; set; }
    public byte[]? KeySecretEncrypted { get; set; }
    public byte[]? WebhookSecretEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsTestMode { get; set; } = true;
    public string HealthStatus { get; set; } = "Unknown";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
