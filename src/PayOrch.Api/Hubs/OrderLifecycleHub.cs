using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PayOrch.Api.Auth;

namespace PayOrch.Api.Hubs;

/// <summary>
/// Live order-status feed for the dashboards. Requires a valid JWT (same
/// auth as the REST endpoints) and auto-joins the caller to a group matching
/// their role, so a broadcast can be targeted without every client filtering
/// client-side:
///   - "role:SuperAdmin" and "role:Admin" — see everything
///   - "subadmin:{userId}" — a Sub-Admin only cares about their own tenants;
///     SignalROrderLifecycleNotifier resolves tenant -> sub_admin_id and
///     broadcasts to this group specifically
/// A merchant/tenant channel isn't wired yet — see the notes in
/// SignalROrderLifecycleNotifier for what that would need.
/// </summary>
[Authorize]
public class OrderLifecycleHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (role is "SuperAdmin" or "Admin")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
        }
        else if (role == "SubAdmin" && userId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"subadmin:{userId}");
        }

        await base.OnConnectedAsync();
    }
}
