namespace PayOrch.Domain.Entities;

/// <summary>
/// One red-flagged identity signal (device, IP, phone, UPI handle, name).
/// Once any of these is flagged, AccessGuardMiddleware (Phase 2) checks
/// every incoming order request against this table and rejects a match
/// across the entire platform — not just the tenant where it was flagged.
/// </summary>
public class BlockedIdentity
{
    public Guid Id { get; set; }
    public string IdentityType { get; set; } = default!; // device | ip | phone | upi | name
    public string IdentityHash { get; set; } = default!;
    public string? Reason { get; set; }
    public Guid? FlaggedBy { get; set; }
    public DateTimeOffset FlaggedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; } // null = permanent
}
