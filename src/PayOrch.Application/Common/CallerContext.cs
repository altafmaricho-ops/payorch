using PayOrch.Domain.Entities;

namespace PayOrch.Application.Common;

/// <summary>Validated identity used by application-level authorization rules.</summary>
public record CallerContext(Guid UserId, UserRole Role);
