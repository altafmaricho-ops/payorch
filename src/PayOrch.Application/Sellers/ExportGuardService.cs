using System.Text.Json;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Sellers;

/// <summary>
/// "Sub-Admin CSV export = blocked at API level, not just UI." This is the
/// single choke point every export endpoint calls before doing any work —
/// so the block lives in one place, and every attempt (allowed or denied)
/// lands in audit_log, per the brief's requirement to log denials too.
/// </summary>
public class ExportGuardService
{
    private readonly IUnitOfWork _uow;

    public ExportGuardService(IUnitOfWork uow) => _uow = uow;

    public async Task<bool> CheckAndAuditAsync(CallerContext caller, string exportType, CancellationToken ct = default)
    {
        var allowed = caller.Role is UserRole.SuperAdmin or UserRole.Admin;

        await _uow.WriteAuditLogAsync(
            caller.UserId,
            allowed ? "export" : "export_attempt_denied",
            "Export",
            exportType,
            JsonSerializer.Serialize(new { exportType }),
            allowed,
            ct);

        await _uow.SaveChangesAsync(ct);
        return allowed;
    }
}
