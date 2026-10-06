namespace PayOrch.Domain.Entities;

public enum UserRole
{
    SuperAdmin = 1,
    Admin = 2,
    SubAdmin = 3
}

public enum UserStatus
{
    PendingApproval = 1,
    Active = 2,
    Suspended = 3,
    Blocked = 4,
    Rejected = 5
}

public class AppUser
{
    public Guid Id { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.PendingApproval;
    public Guid? ParentId { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public decimal CreditBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsBlocked { get; set; }
    public string? StatusReason { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public int TokenVersion { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
