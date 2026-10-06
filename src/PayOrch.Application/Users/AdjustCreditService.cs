using System.Text.Json;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Users;

public record AdjustCreditCommand(CallerContext Caller, Guid TargetUserId, decimal Delta, string Reason);
public record AdjustCreditResponse(bool Success, string? ErrorMessage, decimal? NewBalance);

public class AdjustCreditService
{
    private readonly IUnitOfWork _uow;
    public AdjustCreditService(IUnitOfWork uow) => _uow = uow;

    public async Task<AdjustCreditResponse> HandleAsync(AdjustCreditCommand cmd, CancellationToken ct = default)
    {
        if (cmd.Delta == 0) return new(false, "Credit adjustment cannot be zero.", null);
        var target = await _uow.GetUserByIdAsync(cmd.TargetUserId, ct);
        if (target is null) return new(false, "Target user not found.", null);
        if (target.Role == UserRole.SuperAdmin) return new(false, "Super Admin credit cannot be adjusted here.", null);

        var isDirectParent = target.ParentId == cmd.Caller.UserId;
        if (!isDirectParent && cmd.Caller.Role != UserRole.SuperAdmin)
        {
            await Denied(cmd, target, "You can only adjust credit for your direct sub-accounts.", ct);
            return new(false, "You can only adjust credit for your direct sub-accounts.", null);
        }

        var newBalance = target.CreditBalance + cmd.Delta;
        if (newBalance < 0) return new(false, "Adjustment would take target balance negative.", null);
        if (newBalance > target.CreditLimit) return new(false, $"Adjustment exceeds this user's credit limit of {target.CreditLimit}.", null);

        if (cmd.Caller.Role != UserRole.SuperAdmin)
        {
            var callerUser = await _uow.GetUserByIdAsync(cmd.Caller.UserId, ct);
            if (callerUser is null) return new(false, "Caller account not found.", null);
            if (cmd.Delta > 0 && callerUser.CreditBalance < cmd.Delta)
                return new(false, "Your available credit is insufficient for this allocation.", null);
            if (cmd.Delta < 0 && callerUser.CreditBalance - cmd.Delta > callerUser.CreditLimit)
                return new(false, "Returning this credit would exceed your credit limit.", null);

            callerUser.CreditBalance -= cmd.Delta;
            callerUser.UpdatedAt = DateTimeOffset.UtcNow;
            await _uow.UpdateUserAsync(callerUser, ct);
        }

        target.CreditBalance = newBalance;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _uow.UpdateUserAsync(target, ct);
        await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "adjust_credit", "AppUser", target.Id.ToString(),
            JsonSerializer.Serialize(new { delta = cmd.Delta, reason = cmd.Reason }), true, ct);
        await _uow.SaveChangesAsync(ct);
        return new(true, null, newBalance);
    }

    private async Task Denied(AdjustCreditCommand cmd, AppUser target, string reason, CancellationToken ct)
    {
        await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "adjust_credit_denied", "AppUser", target.Id.ToString(),
            JsonSerializer.Serialize(new { reason }), false, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
