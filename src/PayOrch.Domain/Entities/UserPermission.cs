namespace PayOrch.Domain.Entities;

public class UserPermission
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PermissionCode { get; set; } = default!;
    public Guid? TenantId { get; set; }
    public bool Granted { get; set; } = true;
}
