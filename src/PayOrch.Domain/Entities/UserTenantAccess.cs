namespace PayOrch.Domain.Entities;

public class UserTenantAccess
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}
