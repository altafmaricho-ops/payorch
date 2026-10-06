namespace PayOrch.Application.Masking;

/// <summary>
/// Simple, deterministic masking. Kept in Application (not Infrastructure)
/// so every use case can depend on the interface without a DB round-trip
/// for the VPA-masking part; merchant-code lookup does hit persistence via
/// the injected store delegate.
/// </summary>
public class IdentityMasker : IIdentityMasker
{
    private readonly Func<Guid, CancellationToken, Task<string>> _merchantCodeLookup;

    public IdentityMasker(Func<Guid, CancellationToken, Task<string>> merchantCodeLookup)
    {
        _merchantCodeLookup = merchantCodeLookup;
    }

    public string MaskVpa(string vpa)
    {
        if (string.IsNullOrWhiteSpace(vpa) || !vpa.Contains('@'))
            return "****";

        var parts = vpa.Split('@', 2);
        var local = parts[0];
        var domain = parts[1];

        var visible = local.Length <= 4 ? local[..Math.Min(2, local.Length)] : local[..4];
        return $"{visible}****@{domain}";
    }

    public Task<string> GetMerchantCodeAsync(Guid tenantId, CancellationToken ct = default)
        => _merchantCodeLookup(tenantId, ct);
}
