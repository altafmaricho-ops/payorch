namespace PayOrch.Domain.Entities;

public enum TransferStatus
{
    Pending = 1,
    Processed = 2,
    Failed = 3,
    Reversed = 4
}

/// <summary>
/// BhandaarBox's record of a Razorpay Route transfer created against an
/// order — the mechanism that sends the seller's share of a customer's
/// payment directly to that seller's Linked Account. Razorpay remains the
/// system of record for whether funds actually settled.
/// </summary>
public class SellerTransfer
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SellerId { get; set; }
    public string? PspTransferId { get; set; }
    public decimal SellerAmount { get; set; }
    public decimal PlatformCommission { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}
