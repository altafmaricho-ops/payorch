using PayOrch.Application.Orders;

namespace PayOrch.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/merchant/orders").WithTags("Orders");

        // Merchant (siteA) creates a deposit order. Auth is API key in header
        // rather than a cookie/JWT session — merchants are Tenants, not AppUsers.
        group.MapPost("/", async (
            CreateOrderRequestDto dto,
            HttpRequest req,
            CreateOrderService service,
            CancellationToken ct) =>
        {
            if (!req.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
                return Results.Unauthorized();

            var ip = req.HttpContext.Connection.RemoteIpAddress?.ToString();

            var result = await service.HandleAsync(new CreateOrderCommand(
                ApiKey: apiKey.ToString(),
                MerchantOrderRef: dto.MerchantOrderRef,
                Amount: dto.Amount,
                SellerId: dto.SellerId,
                CustomerRef: dto.CustomerRef,
                DeviceFingerprint: dto.DeviceFingerprint,
                IpAddress: ip,
                PaymentMethod: dto.PaymentMethod,
                CustomerName: dto.CustomerName,
                CustomerEmail: dto.CustomerEmail,
                CustomerPhone: dto.CustomerPhone,
                MetadataJson: dto.MetadataJson), ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Ok(new
            {
                orderId = result.OrderId,
                upiIntentUri = result.UpiIntentUri,
                qrPngBase64 = result.QrPayloadBase64,
                expiresAt = result.ExpiresAt
            });
        });
    }
}

public record CreateOrderRequestDto(string MerchantOrderRef, decimal Amount, Guid? SellerId, string? CustomerRef, string? DeviceFingerprint, string? PaymentMethod, string? CustomerName, string? CustomerEmail, string? CustomerPhone, string? MetadataJson);
