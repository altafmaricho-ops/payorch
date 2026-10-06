using PayOrch.Application.Orders;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Endpoints;

/// <summary>
/// Stable, merchant-facing gateway contract. Merchants integrate with this
/// surface and never need to know which PSP/routing rule processed a payment.
/// Authentication is the tenant API key over HTTPS; merchant webhooks are
/// signed by the tenant callback secret.
/// </summary>
public static class MerchantV1Endpoints
{
    public static void MapMerchantV1Endpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/payments").WithTags("Merchant Payments");

        group.MapPost("", async (
            MerchantCreatePaymentRequest request,
            HttpRequest http,
            CreateOrderService service,
            CancellationToken ct) =>
        {
            if (!http.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.MerchantOrderId))
                return Results.BadRequest(new { error = "merchantOrderId is required." });
            if (!string.IsNullOrWhiteSpace(request.Currency) && !string.Equals(request.Currency, "INR", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only INR is currently supported." });

            var ip = http.HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await service.HandleAsync(new CreateOrderCommand(
                ApiKey: apiKey.ToString(),
                MerchantOrderRef: request.MerchantOrderId,
                Amount: request.Amount,
                SellerId: request.SellerId,
                CustomerRef: request.CustomerRef,
                DeviceFingerprint: request.DeviceFingerprint,
                IpAddress: ip,
                PaymentMethod: request.PaymentMethod,
                CustomerName: request.Customer?.Name,
                CustomerEmail: request.Customer?.Email,
                CustomerPhone: request.Customer?.Phone,
                MetadataJson: request.MetadataJson), ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Ok(new
            {
                paymentId = result.OrderId,
                merchantOrderId = request.MerchantOrderId.Trim(),
                status = "IntentLaunched",
                amount = request.Amount,
                currency = "INR",
                paymentMethod = request.PaymentMethod ?? "upi",
                checkout = new
                {
                    upiIntentUri = result.UpiIntentUri,
                    qrPngBase64 = result.QrPayloadBase64,
                    expiresAt = result.ExpiresAt
                },
                message = "Payment created. Use the checkout payload or query the payment status API."
            });
        });

        group.MapGet("/{paymentId:guid}", async (
            Guid paymentId,
            HttpRequest http,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var tenant = await AuthenticateTenant(http, uow, ct);
            if (tenant is null) return Results.Unauthorized();

            var order = await uow.GetOrderByIdAsync(paymentId, ct);
            if (order is null || order.TenantId != tenant.Id) return Results.NotFound();
            return Results.Ok(ToResponse(order));
        });

        group.MapGet("/by-reference/{merchantOrderId}", async (
            string merchantOrderId,
            HttpRequest http,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var tenant = await AuthenticateTenant(http, uow, ct);
            if (tenant is null) return Results.Unauthorized();
            var order = await uow.GetOrderByTenantAndMerchantRefAsync(tenant.Id, merchantOrderId.Trim(), ct);
            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        });
    }

    private static async Task<Tenant?> AuthenticateTenant(HttpRequest request, IUnitOfWork uow, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            return null;
        var tenant = await uow.GetTenantByApiKeyAsync(apiKey.ToString().Trim(), ct);
        return tenant is { IsActive: true, Status: TenantStatus.Active } ? tenant : null;
    }

    private static object ToResponse(Order o) => new
    {
        paymentId = o.Id,
        merchantOrderId = o.MerchantOrderRef,
        status = o.Status.ToString(),
        amount = o.Amount,
        currency = o.Currency,
        psp = o.PspCode,
        pspOrderId = o.PspOrderId,
        pspPaymentId = o.PspPaymentId,
        failureReason = o.FailureReason,
        createdAt = o.CreatedAt,
        expiresAt = o.ExpiresAt,
        completedAt = o.CompletedAt
    };
}

public record MerchantCreatePaymentRequest(
    string MerchantOrderId,
    decimal Amount,
    string? Currency,
    string? PaymentMethod,
    MerchantCustomer? Customer,
    string? CustomerRef,
    string? DeviceFingerprint,
    Guid? SellerId,
    string? MetadataJson,
    string? ReturnUrl);

public record MerchantCustomer(string? Name, string? Email, string? Phone);
