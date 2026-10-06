namespace PayOrch.Application.Common;

/// <summary>
/// Pushes a live order-status update to whichever dashboards are watching.
/// Application stays framework-agnostic — SignalR is an ASP.NET Core/Api
/// concern, so the real implementation (backed by IHubContext) lives in
/// PayOrch.Api and is injected here as this interface. A no-op
/// implementation could stand in for tests or a non-web host.
/// </summary>
public interface IOrderLifecycleNotifier
{
    Task NotifyOrderStatusChangedAsync(Guid tenantId, Guid orderId, string status, decimal amount, string currency, CancellationToken ct = default);
}
