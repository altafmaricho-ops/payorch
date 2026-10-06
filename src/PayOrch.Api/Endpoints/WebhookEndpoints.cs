using PayOrch.Application.Orders;

namespace PayOrch.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        // Deliberately NOT behind the same auth pipeline as merchant/admin
        // routes — PSPs call this directly. Signature verification inside
        // WebhookIngestService is what actually authenticates the caller.
        // In production this route should also sit behind an IP allowlist
        // for the PSP's published webhook source ranges.
        //
        // This handler ONLY verifies + persists (fast). Order/wallet/ledger/
        // transfer/relay work happens asynchronously in WebhookProcessingWorker
        // so a downstream slowdown never risks this endpoint timing out and
        // triggering a PSP retry storm.
        app.MapPost("/webhooks/{pspCode}", async (
            string pspCode,
            HttpRequest req,
            WebhookIngestService service,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var rawBody = await reader.ReadToEndAsync(ct);

            var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var (accepted, reason) = await service.HandleAsync(pspCode, rawBody, headers, ct);

            // Always 200 on anything we accepted-and-logged (including
            // duplicates) so the PSP stops retrying; only a genuinely
            // invalid signature gets a 400.
            return accepted ? Results.Ok(new { status = reason }) : Results.BadRequest(new { status = reason });
        }).WithTags("Webhooks");
    }
}
