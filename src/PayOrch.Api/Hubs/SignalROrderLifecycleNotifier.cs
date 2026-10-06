using Microsoft.AspNetCore.SignalR;
using PayOrch.Application.Common;

namespace PayOrch.Api.Hubs;

/// <summary>
/// Broadcasts to Super Admin/Admin always, plus the specific Sub-Admin group
/// that owns the tenant involved (resolved via tenants.sub_admin_id) — so a
/// Sub-Admin's live dashboard only ever shows orders for their own book,
/// mirroring the same data wall RLS enforces for REST reads.
///
/// NOT YET WIRED: a per-tenant/merchant group (so BhandaarBox's own ops
/// screen, or a future Merchant Portal, gets only its own live feed). Add a
/// "tenant:{tenantId}" group the same way once tenant-level login exists —
/// today Tenants authenticate via API key, not a JWT, so they have no
/// identity to join a Hub group with yet.
/// </summary>
public class SignalROrderLifecycleNotifier : IOrderLifecycleNotifier
{
    private readonly IHubContext<OrderLifecycleHub> _hub;
    private readonly IUnitOfWork _uow;

    public SignalROrderLifecycleNotifier(IHubContext<OrderLifecycleHub> hub, IUnitOfWork uow)
    {
        _hub = hub;
        _uow = uow;
    }

    public async Task NotifyOrderStatusChangedAsync(Guid tenantId, Guid orderId, string status, decimal amount, string currency, CancellationToken ct = default)
    {
        var payload = new
        {
            tenantId,
            orderId,
            status,
            amount,
            currency,
            at = DateTimeOffset.UtcNow
        };

        var tenant = await _uow.GetTenantByIdAsync(tenantId, ct);

        var groups = new List<string> { "role:SuperAdmin", "role:Admin" };
        if (tenant is not null) groups.Add($"subadmin:{tenant.SubAdminId}");

        await _hub.Clients.Groups(groups).SendAsync("orderStatusChanged", payload, ct);
    }
}
