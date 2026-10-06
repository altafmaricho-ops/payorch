using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PayOrch.Domain.Entities;
using PayOrch.Infrastructure.Persistence;

namespace PayOrch.Api.Bootstrap;

/// <summary>
/// Applies EF migrations and ensures the configured Super Admin exists.
/// Idempotent: an empty/recreated database becomes usable automatically.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        var section = configuration.GetSection("BootstrapSuperAdmin");
        if (!section.GetValue<bool>("Enabled"))
            return;

        var email = section["Email"]?.Trim();
        var displayName = section["DisplayName"]?.Trim();
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("BootstrapSuperAdmin is enabled but Email, DisplayName or Password is missing in appsettings.json.");
        if (password.Length < 8)
            throw new InvalidOperationException("BootstrapSuperAdmin password must contain at least 8 characters.");

        var existing = await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        var hasher = new PasswordHasher<AppUser>();

        if (existing is not null)
        {
            var changed = false;
            var resetPassword = section.GetValue<bool>("ResetPasswordOnStartup");

            // Local development recovery: the configured bootstrap identity is
            // authoritative when ResetPasswordOnStartup=true. This guarantees
            // that a reused local database cannot leave the developer locked
            // out because an older account with the same email had a different
            // role, status, or password. Production must leave this disabled.
            if (resetPassword && existing.Role != UserRole.SuperAdmin)
            {
                existing.Role = UserRole.SuperAdmin;
                existing.ParentId = null;
                changed = true;
            }

            if (existing.Role == UserRole.SuperAdmin)
            {

                if (existing.Status != UserStatus.Active)
                {
                    existing.Status = UserStatus.Active;
                    existing.IsBlocked = false;
                    existing.StatusReason = null;
                    existing.ApprovedAt ??= DateTimeOffset.UtcNow;
                    changed = true;
                }

                // Development/local recovery: when explicitly enabled, keep the
                // configured bootstrap password synchronized with the existing
                // Super Admin instead of silently leaving an old password in DB.
                // This is disabled by default and should remain disabled in production.
                if (resetPassword)
                {
                    var verify = hasher.VerifyHashedPassword(existing, existing.PasswordHash, password);
                    if (verify == PasswordVerificationResult.Failed)
                    {
                        existing.PasswordHash = hasher.HashPassword(existing, password);
                        changed = true;
                    }
                    else if (verify == PasswordVerificationResult.SuccessRehashNeeded)
                    {
                        existing.PasswordHash = hasher.HashPassword(existing, password);
                        changed = true;
                    }
                }

                if (changed)
                {
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            return;
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Role = UserRole.SuperAdmin,
            Status = UserStatus.Active,
            ParentId = null,
            Email = email,
            DisplayName = displayName,
            CreditLimit = 9999999999999999.99m,
            CreditBalance = 0,
            IsBlocked = false,
            TokenVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ApprovedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        db.AuditLog.Add(new AuditLogEntry
        {
            ActorId = user.Id,
            Action = "bootstrap_superadmin",
            TargetType = "AppUser",
            TargetId = user.Id.ToString(),
            Allowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
