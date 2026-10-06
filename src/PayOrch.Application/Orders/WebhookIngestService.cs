using PayOrch.Application.Common;
using PayOrch.Application.Masking;
using PayOrch.Application.Psp;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Orders;

/// <summary>
/// Phase 1 of webhook handling — everything that MUST happen inline in the
/// HTTP request, and nothing more. Verifies the PSP signature, parses the
/// fields the background worker will need, persists one webhook_events row,
/// and returns. No order/wallet/ledger/transfer/relay work happens here —
/// that's WebhookProcessingService's job, run out-of-band by
/// WebhookProcessingWorker. This split exists so a slow downstream step
/// (a stuck seller-transfer call, a busy ledger row) never risks Razorpay's
/// webhook delivery timing out and retrying, or a traffic spike backing up
/// the request pipeline.
/// </summary>
public class WebhookIngestService
{
    private readonly IUnitOfWork _uow;
    private readonly IPspAdapterResolver _pspResolver;
    private readonly IIdentityMasker _masker;

    public WebhookIngestService(IUnitOfWork uow, IPspAdapterResolver pspResolver, IIdentityMasker masker)
    {
        _uow = uow;
        _pspResolver = pspResolver;
        _masker = masker;
    }

    public async Task<(bool Accepted, string Reason)> HandleAsync(
        string pspCode, string rawBody, IDictionary<string, string> headers, CancellationToken ct = default)
    {
        IPspAdapter adapter;
        try { adapter = _pspResolver.GetByCode(pspCode.Trim().ToLowerInvariant()); }
        catch (InvalidOperationException) { return (false, "unsupported_psp"); }
        var parsed = await adapter.VerifyAndParseWebhookAsync(rawBody, headers, ct);

        if (!parsed.SignatureValid || parsed.PspEventId is null)
        {
            // Never persist an unverified payload at all — log-and-drop.
            return (false, "signature_invalid");
        }

        if (await _uow.WebhookEventExistsAsync(pspCode, parsed.PspEventId, ct))
            return (true, "duplicate_ignored"); // idempotent — PSP callbacks retry by design

        var outcome = parsed.Status switch
        {
            PspOrderStatus.Success => PspEventOutcome.Success,
            PspOrderStatus.Failed => PspEventOutcome.Failed,
            _ => PspEventOutcome.Unknown
        };

        await _uow.AddWebhookEventAsync(new WebhookEvent
        {
            Id = Guid.NewGuid(),
            PspCode = pspCode,
            PspEventId = parsed.PspEventId,
            PayloadJson = rawBody,
            SignatureValid = true,
            PspOrderId = parsed.PspOrderId,
            PspPaymentId = parsed.PspPaymentId,
            Outcome = outcome,
            PayerVpaMasked = parsed.PayerVpaMasked is not null ? _masker.MaskVpa(parsed.PayerVpaMasked) : null,
            ReceivedAt = DateTimeOffset.UtcNow
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return (true, "queued");
    }
}
