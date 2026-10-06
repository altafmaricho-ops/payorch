namespace PayOrch.Domain.Entities;

public enum OrderStatus
{
    Created = 1,
    IntentLaunched = 2,
    Succeeded = 3,
    Failed = 4,
    Expired = 5
}

/// <summary>
/// One deposit collection attempt. PspOrderId ties this row to Razorpay's
/// (or another PSP's) own order/payment identifier. PayerVpaMasked is
/// stored already-masked — the raw payer VPA is never persisted here.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SellerId { get; set; }
    public string MerchantOrderRef { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public OrderStatus Status { get; set; } = OrderStatus.Created;
    public string PspCode { get; set; } = default!;
    public string? PspOrderId { get; set; }
    public string? PspPaymentId { get; set; }
    public string? PayerVpaMasked { get; set; }
    public string? CustomerRef { get; set; }
    public string? DeviceFingerprint { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? MetadataJson { get; set; }
    public Guid? RoutingRuleId { get; set; }
    public Guid? PaymentProviderId { get; set; }
    public string? FailureReason { get; set; }
}
