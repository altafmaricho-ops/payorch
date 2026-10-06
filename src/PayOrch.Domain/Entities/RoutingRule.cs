namespace PayOrch.Domain.Entities;

public class RoutingRule
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid PaymentProviderId { get; set; }
    public decimal MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    public Guid? FallbackProviderId { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
