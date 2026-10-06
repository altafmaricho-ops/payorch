namespace PayOrch.Domain.Entities;

public enum TenantStatus
{
    PendingApproval = 1,
    Active = 2,
    Suspended = 3,
    Blocked = 4,
    Rejected = 5
}

public class Tenant
{
    public Guid Id { get; set; }
    public Guid SubAdminId { get; set; }
    public string MerchantCode { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string Vertical { get; set; } = default!;
    public string CallbackUrl { get; set; } = default!;
    public decimal CommissionPercent { get; set; }
    public string ApiKey { get; set; } = default!;
    public string ApiSecretHash { get; set; } = default!;
    public string CallbackSecret { get; set; } = default!;
    public TenantStatus Status { get; set; } = TenantStatus.PendingApproval;
    public bool IsActive { get; set; }
    public string? StatusReason { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
