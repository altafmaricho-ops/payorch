namespace PayOrch.Domain.Entities;

/// <summary>
/// Append-only. Every privileged action — export attempts (including
/// denials), credit adjustments, red-flag actions, seller onboarding,
/// status changes — writes one row here. Never updated, never deleted.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = default!;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? MetadataJson { get; set; }
    public bool Allowed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
