using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PayOrch.Application.Psp;

namespace PayOrch.Infrastructure.Psp.Razorpay;

/// <summary>
/// Concrete IPspAdapter for Razorpay. Uses Razorpay's Payment Links API
/// (upi_link: true) rather than raw VPAs — Razorpay is the licensed PA,
/// generates its own collection VPA per link, and the returned short_url
/// auto-detects mobile (opens the UPI app intent) vs desktop (renders a
/// QR). We never see or store a raw settlement VPA on our side.
///
/// Docs reference (for the engineer wiring real credentials):
/// https://razorpay.com/docs/payment-links/upi-payment-links/
/// https://razorpay.com/docs/webhooks/
/// </summary>
public class RazorpayPspAdapter : IPspAdapter
{
    public string PspCode => "razorpay";

    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;

    public RazorpayPspAdapter(HttpClient http, IOptions<RazorpayOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);

        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }

    public async Task<PspOrderResult> CreateOrderAsync(PspOrderRequest request, CancellationToken ct = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.OrderExpiryMinutes);

        var payload = new
        {
            amount = (int)Math.Round(request.Amount * 100), // paise
            currency = request.Currency,
            accept_partial = false,
            description = $"Order {request.MerchantOrderRef}",
            reference_id = request.MerchantOrderRef,
            upi_link = true,
            expire_by = expiresAt.ToUnixTimeSeconds(),
            notify = new { sms = false, email = false },
            callback_url = request.CallbackNotifyUrl,
            callback_method = "get"
        };

        using var resp = await _http.PostAsJsonAsync("/v1/payment_links", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return new PspOrderResult(false, null, null, null, expiresAt, $"Razorpay error ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var pspOrderId = root.GetProperty("id").GetString();
        var shortUrl = root.TryGetProperty("short_url", out var su) ? su.GetString() : null;

        // short_url is what mobile browsers auto-resolve into a UPI intent
        // launch, and what desktop renders as a page with a scannable QR.
        // If a dedicated QR PNG is needed server-side instead, use
        // Razorpay's QR Code API (/v1/payments/qr_codes) as an alternative
        // creation path — left as a follow-up, not required for Phase 1.
        return new PspOrderResult(true, pspOrderId, shortUrl, null, expiresAt, null);
    }

    public Task<PspWebhookResult> VerifyAndParseWebhookAsync(string rawBody, IDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!headers.TryGetValue("X-Razorpay-Signature", out var signature) || string.IsNullOrEmpty(signature))
            return Task.FromResult(new PspWebhookResult(false, null, null, null, PspOrderStatus.Unknown, null));

        var expected = ComputeHmacSha256Hex(rawBody, _options.WebhookSecret);
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));

        if (!valid)
            return Task.FromResult(new PspWebhookResult(false, null, null, null, PspOrderStatus.Unknown, null));

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var eventId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var eventType = root.GetProperty("event").GetString() ?? string.Empty;

        // payment_link.paid | payment_link.expired | payment.captured | payment.failed
        var payload = root.GetProperty("payload");

        string? pspOrderId = null;
        string? pspPaymentId = null;
        string? payerVpa = null;
        var status = PspOrderStatus.Unknown;

        if (payload.TryGetProperty("payment_link", out var plEntity) && plEntity.TryGetProperty("entity", out var plData))
        {
            pspOrderId = plData.GetProperty("id").GetString();
        }

        if (payload.TryGetProperty("payment", out var pEntity) && pEntity.TryGetProperty("entity", out var pData))
        {
            pspPaymentId = pData.TryGetProperty("id", out var payIdEl) ? payIdEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(pspOrderId) && pData.TryGetProperty("order_id", out var orderIdEl))
                pspOrderId = orderIdEl.GetString();
            if (pData.TryGetProperty("vpa", out var vpaEl))
                payerVpa = vpaEl.GetString();
        }

        status = eventType switch
        {
            "payment_link.paid" => PspOrderStatus.Success,
            "payment.captured" => PspOrderStatus.Success,
            "payment_link.expired" => PspOrderStatus.Failed,
            "payment.failed" => PspOrderStatus.Failed,
            _ => PspOrderStatus.Unknown
        };

        return Task.FromResult(new PspWebhookResult(true, eventId, pspOrderId, pspPaymentId, status, payerVpa));
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/v1/payment_links?count=1", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<PspOrderStatus> GetOrderStatusAsync(string pspOrderId, CancellationToken ct = default)
    {
        // Reconciliation fallback path — polls Razorpay directly when no
        // webhook has arrived within the expected window.
        using var resp = await _http.GetAsync($"/v1/payment_links/{pspOrderId}", ct);
        if (!resp.IsSuccessStatusCode) return PspOrderStatus.Unknown;

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var status = doc.RootElement.GetProperty("status").GetString();

        return status switch
        {
            "paid" => PspOrderStatus.Success,
            "expired" or "cancelled" => PspOrderStatus.Failed,
            "created" or "partially_paid" => PspOrderStatus.Pending,
            _ => PspOrderStatus.Unknown
        };
    }

    public async Task<PspLinkedAccountResult> CreateLinkedAccountAsync(PspLinkedAccountRequest request, CancellationToken ct = default)
    {
        // Razorpay Route "Linked Account" (a.k.a. sub-merchant) creation.
        // Route requires your Razorpay account to have Route enabled first
        // (a one-time activation Razorpay grants on request — not an RBI
        // license on your end, since Razorpay's own PA license covers it).
        // Docs: https://razorpay.com/docs/route/onboard-linked-accounts/
        var payload = new
        {
            email = request.ContactEmail,
            phone = request.ContactPhone,
            type = "route",
            reference_id = request.ReferenceId,
            legal_business_name = request.BusinessName,
            business_type = "individual", // Admin can override to proprietorship/partnership/etc per seller later
            contact_name = request.BusinessName,
            profile = new
            {
                category = "ecommerce",
                subcategory = "marketplace",
                addresses = new { }
            }
        };

        using var resp = await _http.PostAsJsonAsync("/v2/accounts", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return new PspLinkedAccountResult(false, null, null, $"Razorpay error ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accountId = root.GetProperty("id").GetString();
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : "created";

        return new PspLinkedAccountResult(true, accountId, status, null);
    }

    public async Task<PspTransferResult> CreateTransferAsync(PspTransferRequest request, CancellationToken ct = default)
    {
        // Splits a captured payment: seller's share moves to their Linked
        // Account, the remainder (platform commission) stays with us.
        // Docs: https://razorpay.com/docs/route/split-settlements-on-invoices/
        var payload = new
        {
            account = request.PspLinkedAccountId,
            amount = (int)Math.Round(request.SellerAmount * 100), // paise
            currency = request.Currency,
            on_hold = false
        };

        using var resp = await _http.PostAsJsonAsync($"/v1/payments/{request.PspPaymentId}/transfers", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return new PspTransferResult(false, null, $"Razorpay error ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        // Razorpay returns { "items": [ { "id": "trf_...", ... } ] } for this endpoint.
        var transferId = doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0
            ? items[0].GetProperty("id").GetString()
            : doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        return new PspTransferResult(true, transferId, null);
    }

    public async Task<PspRefundResult> CreateRefundAsync(PspRefundRequest request, CancellationToken ct = default)
    {
        var payload = new { amount = (int)Math.Round(request.Amount * 100), speed = "normal", notes = new { reason = request.Reason ?? string.Empty } };
        using var resp = await _http.PostAsJsonAsync($"/v1/payments/{request.PspPaymentId}/refund", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return new PspRefundResult(false, null, $"Razorpay error ({(int)resp.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        return new PspRefundResult(true, id, null);
    }

    private static string ComputeHmacSha256Hex(string message, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
