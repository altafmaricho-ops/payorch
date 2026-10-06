namespace PayOrch.Domain.Entities;

public enum PspEventOutcome
{
    Unknown = 0,
    Success = 1,
    Failed = 2
}

/// <summary>
/// Inbox row for every inbound PSP webhook. (PspCode, PspEventId) is unique
/// so retried callbacks are naturally idempotent at the DB layer.
///
/// Split into two phases so the webhook HTTP endpoint stays fast under load:
/// 1. INGEST (API request path): verify signature, parse the fields below,
///    insert this row, return 200 immediately. Nothing here touches the
///    orders/wallets/ledger tables — Razorpay's retry timeout never waits
///    on our downstream processing.
/// 2. PROCESS (background worker, see WebhookProcessingWorker): claims
///    unprocessed rows with SELECT ... FOR UPDATE SKIP LOCKED so multiple
///    worker instances can run concurrently without double-processing,
///    then does the order update / ledger credit / seller transfer / relay
///    enqueue that used to happen inline in the request.
/// </summary>
public class WebhookEvent
{
    public Guid Id { get; set; }
    public string PspCode { get; set; } = default!;
    public string PspEventId { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public bool SignatureValid { get; set; }

    // Parsed at ingest time so the worker never needs to re-verify or
    // re-parse the raw payload (and never needs the original request headers).
    public string? PspOrderId { get; set; }
    public string? PspPaymentId { get; set; }
    public PspEventOutcome Outcome { get; set; } = PspEventOutcome.Unknown;
    public string? PayerVpaMasked { get; set; }   // already masked at ingest — raw VPA never reaches this column

    // Claim/lease bookkeeping for the background worker.
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Guid? OrderId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public enum RelayStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3,
    Exhausted = 4,
    Processing = 5
}

/// <summary>
/// Outbound notification queued to relay a verified payment event to the
/// tenant's own registered CallbackUrl (siteA collects -> we relay to siteB).
/// </summary>
public class CallbackRelay
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public string TargetUrl { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public RelayStatus Status { get; set; } = RelayStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int? LastResponseCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
