using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Users;

public record CreateSubordinateUserCommand(CallerContext Caller, string Email, string DisplayName, string PasswordHash, decimal InitialCreditLimit);

public record CreateSubordinateUserResponse(bool Success, string? ErrorMessage, Guid? UserId);

/// <summary>
/// Enforces the hierarchy rule from the brief: Super Admin creates Admins;
/// Admin creates Sub-Admins; nobody creates a peer or a Super Admin via
/// this path. A Sub-Admin cannot create anyone (they manage VPAs/turnover,
/// not the org chart).
/// </summary>
public class CreateSubordinateUserService
{
    private readonly IUnitOfWork _uow;

    public CreateSubordinateUserService(IUnitOfWork uow) => _uow = uow;

    public async Task<CreateSubordinateUserResponse> HandleAsync(CreateSubordinateUserCommand cmd, CancellationToken ct = default)
    {
        UserRole childRole;
        switch (cmd.Caller.Role)
        {
            case UserRole.SuperAdmin:
                childRole = UserRole.Admin;
                break;
            case UserRole.Admin:
                childRole = UserRole.SubAdmin;
                break;
            default:
                await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "create_user_denied", "AppUser", null, null, allowed: false, ct);
                await _uow.SaveChangesAsync(ct);
                return new CreateSubordinateUserResponse(false, "Sub-Admins cannot create users.", null);
        }

        if (cmd.InitialCreditLimit < 0)
            return new CreateSubordinateUserResponse(false, "Initial credit limit cannot be negative.", null);

        var existing = await _uow.GetUserByEmailAsync(cmd.Email.Trim(), ct);
        if (existing is not null)
            return new CreateSubordinateUserResponse(false, "A user with this email already exists.", null);

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Role = childRole,
            Status = UserStatus.PendingApproval,
            ParentId = cmd.Caller.UserId,
            Email = cmd.Email,
            PasswordHash = cmd.PasswordHash,
            DisplayName = cmd.DisplayName,
            CreditLimit = cmd.InitialCreditLimit,
            CreditBalance = 0,
            IsBlocked = false,
            TokenVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _uow.AddUserAsync(user, ct);
        await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "create_user", "AppUser", user.Id.ToString(), null, allowed: true, ct);
        await _uow.SaveChangesAsync(ct);

        return new CreateSubordinateUserResponse(true, null, user.Id);
    }
}
