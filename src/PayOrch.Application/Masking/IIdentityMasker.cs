namespace PayOrch.Application.Masking;

/// <summary>
/// Central point for every place a payer handle or merchant identity gets
/// displayed. Nothing upstream of the PSP webhook handler should ever see
/// an unmasked payer VPA — by the time an Order row exists, PayerVpaMasked
/// is already the only form persisted.
/// </summary>
public interface IIdentityMasker
{
    /// <summary>"customer@oksbi" -> "cust****@oksbi"</summary>
    string MaskVpa(string vpa);

    /// <summary>Looks up (or assigns) the generic code a Sub-Admin sees
    /// instead of a tenant's real name, e.g. "Merchant 01".</summary>
    Task<string> GetMerchantCodeAsync(Guid tenantId, CancellationToken ct = default);
}
