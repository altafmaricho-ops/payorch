namespace PayOrch.Domain.Entities;

public enum SellerOnboardingStatus
{
    PendingReview = 1,   // Admin has created the record, not yet sent to PSP
    SubmittedToPsp = 2,  // Linked Account creation requested at Razorpay
    Active = 3,          // Razorpay has activated the linked account — can receive transfers
    Rejected = 4,
    Suspended = 5
}

/// <summary>
/// A local merchant onboarded onto BhandaarBox by an Admin. BusinessName
/// is the real name (hidden from Sub-Admins — see v_sellers_subadmin).
/// UpiCollectionApp is informational only: it records which app the seller
/// happens to use day to day, but plays no role in how money actually
/// moves — that always goes customer -> Razorpay -> PspLinkedAccountId.
/// </summary>
public class Seller
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OnboardedBy { get; set; }
    public string SellerCode { get; set; } = default!;
    public string BusinessName { get; set; } = default!;
    public string ContactPhone { get; set; } = default!;
    public string? ContactEmail { get; set; }
    public string? UpiCollectionApp { get; set; }   // 'gpay_business' | 'phonepe_business' | 'paytm_business' | 'other'
    public string PspCode { get; set; } = "razorpay";
    public string? PspLinkedAccountId { get; set; }
    public decimal CommissionPercent { get; set; }
    public SellerOnboardingStatus Status { get; set; } = SellerOnboardingStatus.PendingReview;
    public string? StatusReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
