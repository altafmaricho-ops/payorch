namespace PayOrch.Domain.Entities;

public class Refund
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid? RequestedBy { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string? PspRefundId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
