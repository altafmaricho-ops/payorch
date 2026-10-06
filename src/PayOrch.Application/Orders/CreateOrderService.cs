using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PayOrch.Application.Common;
using PayOrch.Application.Psp;
using PayOrch.Application.Routing;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Orders;

public record CreateOrderCommand(string ApiKey, string MerchantOrderRef, decimal Amount, Guid? SellerId,
    string? CustomerRef, string? DeviceFingerprint, string? IpAddress, string? PaymentMethod = null,
    string? CustomerName = null, string? CustomerEmail = null, string? CustomerPhone = null, string? MetadataJson = null);

public record CreateOrderResponse(bool Success, string? ErrorMessage, Guid? OrderId, string? UpiIntentUri, string? QrPayloadBase64, DateTimeOffset? ExpiresAt);

public class CreateOrderService
{
    private readonly IUnitOfWork _uow;
    private readonly IPspAdapterResolver _pspResolver;
    private readonly string _webhookNotifyBaseUrl;
    private readonly PaymentRoutingService _routing;

    public CreateOrderService(IUnitOfWork uow, IPspAdapterResolver pspResolver, string webhookNotifyBaseUrl, PaymentRoutingService routing)
    {
        _uow = uow;
        _pspResolver = pspResolver;
        _webhookNotifyBaseUrl = webhookNotifyBaseUrl.TrimEnd('/');
        _routing = routing;
    }

    public async Task<CreateOrderResponse> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var tenant = await _uow.GetTenantByApiKeyAsync(cmd.ApiKey.Trim(), ct);
        if (tenant is null || tenant.Status != TenantStatus.Active || !tenant.IsActive)
            return Fail("Unknown or inactive merchant website.");

        if (string.IsNullOrWhiteSpace(cmd.MerchantOrderRef) || cmd.MerchantOrderRef.Length > 120)
            return Fail("Merchant order reference is required and must be at most 120 characters.");
        if (cmd.Amount <= 0 || cmd.Amount > 10000000m)
            return Fail("Amount must be greater than 0 and no more than ₹1,00,00,000.");
        if (!string.Equals(cmd.PaymentMethod, "upi", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cmd.PaymentMethod) && cmd.PaymentMethod.Length > 32)
            return Fail("Payment method is invalid.");
        if (!string.IsNullOrWhiteSpace(cmd.MetadataJson))
        {
            try { using var _ = JsonDocument.Parse(cmd.MetadataJson); }
            catch { return Fail("MetadataJson must contain valid JSON."); }
        }

        var existing = await _uow.GetOrderByTenantAndMerchantRefAsync(tenant.Id, cmd.MerchantOrderRef.Trim(), ct);
        if (existing is not null)
        {
            if (existing.Amount != cmd.Amount)
                return Fail("This merchant order reference already exists with a different amount.");
            return new CreateOrderResponse(true, null, existing.Id, null, null, existing.ExpiresAt);
        }

        if (await IsBlockedAsync(cmd, ct))
            return Fail("Payment request rejected by platform risk controls.");

        if (cmd.SellerId is Guid sellerId)
        {
            var seller = await _uow.GetSellerByIdAsync(sellerId, ct);
            if (seller is null || seller.TenantId != tenant.Id)
                return Fail("The selected seller does not belong to this website.");
            if (seller.Status != SellerOnboardingStatus.Active)
                return Fail("The selected seller is not active for payment collection.");
        }

        var routing = await _routing.ResolveAsync(tenant.Id, cmd.Amount, cmd.PaymentMethod, ct);
        if (!string.IsNullOrWhiteSpace(routing.Error)) return Fail(routing.Error!);

        IPspAdapter adapter;
        try { adapter = _pspResolver.GetByCode(routing.PspCode); }
        catch (InvalidOperationException)
        {
            if (routing.FallbackProviderId is not Guid fallbackId)
                return Fail($"Payment provider '{routing.PspCode}' is configured but its adapter is not enabled on this deployment.");
            var fallback = await _uow.GetPaymentProviderAsync(fallbackId, ct);
            if (fallback is null || !fallback.IsActive) return Fail("Primary payment provider is unavailable and no active fallback exists.");
            try { adapter = _pspResolver.GetByCode(fallback.ProviderCode); }
            catch (InvalidOperationException) { return Fail("Both the primary and fallback payment provider adapters are unavailable on this deployment."); }
        }

        var pspResult = await adapter.CreateOrderAsync(new PspOrderRequest(
            MerchantOrderRef: cmd.MerchantOrderRef.Trim(), Amount: cmd.Amount, Currency: "INR",
            CustomerRef: cmd.CustomerRef, CallbackNotifyUrl: $"{_webhookNotifyBaseUrl}/webhooks/{adapter.PspCode}"), ct);

        if (!pspResult.Success)
            return Fail(pspResult.ErrorMessage ?? "Payment provider order creation failed.");

        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, SellerId = cmd.SellerId,
            MerchantOrderRef = cmd.MerchantOrderRef.Trim(), Amount = cmd.Amount, Currency = "INR",
            Status = OrderStatus.IntentLaunched, PspCode = adapter.PspCode,
            PspOrderId = pspResult.PspOrderId, CustomerRef = cmd.CustomerRef,
            DeviceFingerprint = cmd.DeviceFingerprint, IpAddress = cmd.IpAddress,
            CustomerName = cmd.CustomerName, CustomerEmail = cmd.CustomerEmail, CustomerPhone = cmd.CustomerPhone,
            MetadataJson = cmd.MetadataJson, RoutingRuleId = routing.RuleId, PaymentProviderId = routing.ProviderId,
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = pspResult.ExpiresAt
        };

        await _uow.AddOrderAsync(order, ct);
        await _uow.SaveChangesAsync(ct);

        return new CreateOrderResponse(true, null, order.Id, pspResult.UpiIntentUri, pspResult.QrPayloadBase64, pspResult.ExpiresAt);
    }

    private async Task<bool> IsBlockedAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var identities = new List<(string Type, string Hash)>();
        Add("device", cmd.DeviceFingerprint);
        Add("ip", cmd.IpAddress);
        Add("phone", cmd.CustomerPhone);
        Add("name", cmd.CustomerName);
        return identities.Count > 0 && await _uow.IsAnyIdentityBlockedAsync(identities, ct);

        void Add(string type, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            identities.Add((type, Sha256Hex(Normalize(raw))));
        }
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static CreateOrderResponse Fail(string message) => new(false, message, null, null, null, null);
}
