using PayOrch.Application.Psp;

namespace PayOrch.Infrastructure.Psp;

/// <summary>
/// Phase 1 registers Razorpay only, marked primary. Adding Cashfree /
/// PhonePe PG / Paytm PG later means registering another IPspAdapter in DI
/// and flipping "primary" via config — no changes to Application or Api.
/// </summary>
public class PspAdapterResolver : IPspAdapterResolver
{
    private readonly IReadOnlyDictionary<string, IPspAdapter> _adapters;
    private readonly string _primaryCode;

    public PspAdapterResolver(IEnumerable<IPspAdapter> adapters, string primaryCode)
    {
        _adapters = adapters.ToDictionary(a => a.PspCode, a => a);
        _primaryCode = primaryCode;
    }

    public IPspAdapter GetPrimary() => GetByCode(_primaryCode);

    public IPspAdapter GetByCode(string pspCode) =>
        _adapters.TryGetValue(pspCode, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No PSP adapter registered for code '{pspCode}'.");

    public IReadOnlyList<IPspAdapter> GetAllActive() => _adapters.Values.ToList();
}
