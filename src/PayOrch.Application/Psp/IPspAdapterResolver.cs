namespace PayOrch.Application.Psp;

/// <summary>
/// Resolves the active adapter (primary, or secondary on failover) without
/// the caller ever knowing which concrete PSP is behind it. Phase 1 registers
/// only Razorpay; adding Cashfree/PhonePe PG/Paytm PG later is additive.
/// </summary>
public interface IPspAdapterResolver
{
    IPspAdapter GetPrimary();
    IPspAdapter GetByCode(string pspCode);
    IReadOnlyList<IPspAdapter> GetAllActive();
}
