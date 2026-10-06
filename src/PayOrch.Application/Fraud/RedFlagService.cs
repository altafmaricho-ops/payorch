using System.Security.Cryptography;
using System.Text;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Fraud;

public record RedFlagCommand(CallerContext Caller, string IdentityType, string RawIdentityValue, string Reason, TimeSpan? TemporaryDuration);

public record RedFlagResponse(bool Success, string? ErrorMessage);

/// <summary>
/// "Only Super Admins and Admins have the authority to manually
/// look up a user profile and flag/blacklist them. Sub-Admins are
/// strictly blocked." Identity values are hashed before storage —
/// blocked_identities never holds a raw phone/UPI/device string.
/// </summary>
public class RedFlagService
{
    private readonly IUnitOfWork _uow;
    private static readonly HashSet<string> ValidTypes = new() { "device", "ip", "phone", "upi", "name" };

    public RedFlagService(IUnitOfWork uow) => _uow = uow;

    public async Task<RedFlagResponse> HandleAsync(RedFlagCommand cmd, CancellationToken ct = default)
    {
        if (cmd.Caller.Role == UserRole.SubAdmin)
        {
            await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "red_flag_denied", "BlockedIdentity", null, null, allowed: false, ct);
            await _uow.SaveChangesAsync(ct);
            return new RedFlagResponse(false, "Sub-Admins cannot red-flag identities.");
        }

        if (!ValidTypes.Contains(cmd.IdentityType))
            return new RedFlagResponse(false, $"Unknown identity type '{cmd.IdentityType}'.");

        var hash = Sha256Hex(Normalize(cmd.RawIdentityValue));

        var existing = await _uow.GetBlockedIdentityAsync(cmd.IdentityType, hash, ct);
        if (existing is null)
        {
            await _uow.AddBlockedIdentityAsync(new BlockedIdentity
            {
                Id = Guid.NewGuid(), IdentityType = cmd.IdentityType, IdentityHash = hash, Reason = cmd.Reason,
                FlaggedBy = cmd.Caller.UserId, FlaggedAt = DateTimeOffset.UtcNow,
                ExpiresAt = cmd.TemporaryDuration is { } dur ? DateTimeOffset.UtcNow.Add(dur) : null
            }, ct);
        }
        else
        {
            existing.Reason = cmd.Reason;
            existing.FlaggedBy = cmd.Caller.UserId;
            existing.FlaggedAt = DateTimeOffset.UtcNow;
            existing.ExpiresAt = cmd.TemporaryDuration is { } dur ? DateTimeOffset.UtcNow.Add(dur) : null;
        }

        await _uow.WriteAuditLogAsync(cmd.Caller.UserId, "red_flag", "BlockedIdentity", cmd.IdentityType, cmd.Reason, allowed: true, ct);
        await _uow.SaveChangesAsync(ct);

        return new RedFlagResponse(true, null);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
