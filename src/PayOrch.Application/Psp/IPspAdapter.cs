namespace PayOrch.Application.Psp;

/// <summary>
/// Every licensed Payment Aggregator/Gateway (Razorpay, Cashfree, PhonePe PG,
/// Paytm PG, ...) implements this. The core orchestration layer never talks
/// to a PSP's raw SDK directly — only through this interface — so adding a
/// failover PSP later is one new class + one config row, never a core change.
/// </summary>
public interface IPspAdapter
{
    /// <summary>Stable code this adapter registers under, e.g. "razorpay".</summary>
    string PspCode { get; }

    /// <summary>
    /// Creates a collection order with the PSP and returns everything needed
    /// to render a UPI Intent link (mobile) or amount-locked QR (desktop).
    /// The PSP's own generated VPA/UPI handle is never persisted raw — only
    /// the masked form (if the PSP even exposes it) is stored.
    /// </summary>
    Task<PspOrderResult> CreateOrderAsync(PspOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies an inbound webhook's signature using this PSP's scheme
    /// (e.g. Razorpay's X-Razorpay-Signature HMAC-SHA256) and, if valid,
    /// parses it into a normalized result. Must be constant-time comparison.
    /// </summary>
    Task<PspWebhookResult> VerifyAndParseWebhookAsync(string rawBody, IDictionary<string, string> headers, CancellationToken ct = default);

    /// <summary>Queries the PSP directly for an order's current status — used by
    /// the reconciliation worker as a fallback when a webhook never arrives.</summary>
    Task<PspOrderStatus> GetOrderStatusAsync(string pspOrderId, CancellationToken ct = default);

    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Marketplace/Route capability: onboard a seller as a Linked Account so
    /// their share of future orders can be auto-transferred to them directly.
    /// Not every PSP adapter needs to support this (a single-tenant PSP
    /// integration wouldn't); Razorpay's does via Route.
    /// </summary>
    Task<PspLinkedAccountResult> CreateLinkedAccountAsync(PspLinkedAccountRequest request, CancellationToken ct = default);

    /// <summary>
    /// Splits a captured order payment, sending the seller's share to their
    /// Linked Account and leaving the commission with the platform account.
    /// </summary>
    Task<PspTransferResult> CreateTransferAsync(PspTransferRequest request, CancellationToken ct = default);

    Task<PspRefundResult> CreateRefundAsync(PspRefundRequest request, CancellationToken ct = default);
}

public record PspLinkedAccountRequest(
    string BusinessName,
    string ContactEmail,
    string ContactPhone,
    string ReferenceId // our sellers.id, for our own reconciliation
);

public record PspLinkedAccountResult(
    bool Success,
    string? PspLinkedAccountId,
    string? Status,          // PSP's own onboarding status string, e.g. "created" | "activated"
    string? ErrorMessage
);

public record PspTransferRequest(
    string PspPaymentId,
    string PspLinkedAccountId,
    decimal SellerAmount,
    decimal PlatformCommission,
    string Currency
);

public record PspTransferResult(
    bool Success,
    string? PspTransferId,
    string? ErrorMessage
);

public record PspRefundRequest(string PspPaymentId, decimal Amount, string Currency, string? Reason);
public record PspRefundResult(bool Success, string? PspRefundId, string? ErrorMessage);

public record PspOrderRequest(
    string MerchantOrderRef,
    decimal Amount,
    string Currency,
    string? CustomerRef,
    string CallbackNotifyUrl // our own webhook ingestion URL, not the tenant's
);

public record PspOrderResult(
    bool Success,
    string? PspOrderId,
    string? UpiIntentUri,
    string? QrPayloadBase64,
    DateTimeOffset ExpiresAt,
    string? ErrorMessage
);

public enum PspOrderStatus
{
    Unknown = 0,
    Pending = 1,
    Success = 2,
    Failed = 3
}

public record PspWebhookResult(
    bool SignatureValid,
    string? PspEventId,
    string? PspOrderId,
    string? PspPaymentId,
    PspOrderStatus Status,
    string? PayerVpaMasked
);
