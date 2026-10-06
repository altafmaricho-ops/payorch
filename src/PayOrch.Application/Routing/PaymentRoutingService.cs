using PayOrch.Application.Common;

namespace PayOrch.Application.Routing;

public record RoutingDecision(Guid? RuleId, Guid? ProviderId, string PspCode, Guid? FallbackProviderId, string? Error);

public class PaymentRoutingService
{
    private readonly IUnitOfWork _uow;
    public PaymentRoutingService(IUnitOfWork uow) => _uow = uow;

    public async Task<RoutingDecision> ResolveAsync(Guid tenantId, decimal amount, string? paymentMethod, CancellationToken ct)
    {
        var rule = await _uow.FindRoutingRuleAsync(tenantId, amount, paymentMethod, ct);
        if (rule is null)
        {
            var provider = await _uow.GetDefaultPaymentProviderAsync(tenantId, ct);
            if (provider is null) return new RoutingDecision(null, null, "", null, "No active payment provider is configured for this website.");
            return new RoutingDecision(null, provider.Id, provider.ProviderCode, null, null);
        }

        var selected = await _uow.GetPaymentProviderAsync(rule.PaymentProviderId, ct);
        if (selected is not null && selected.IsActive)
            return new RoutingDecision(rule.Id, selected.Id, selected.ProviderCode, rule.FallbackProviderId, null);

        if (rule.FallbackProviderId is Guid fallbackId)
        {
            var fallback = await _uow.GetPaymentProviderAsync(fallbackId, ct);
            if (fallback is not null && fallback.IsActive)
                return new RoutingDecision(rule.Id, fallback.Id, fallback.ProviderCode, null, null);
        }

        return new RoutingDecision(rule.Id, null, "", rule.FallbackProviderId, "The selected payment provider is inactive or missing and no active fallback is available.");
    }
}
