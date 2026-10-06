using Microsoft.AspNetCore.Identity;
using PayOrch.Api.Auth;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginDto dto, IUnitOfWork uow, JwtTokenService jwt, CancellationToken ct) =>
        {
            var email = dto.Email.Trim();
            var user = await uow.GetUserByEmailAsync(email, ct);
            if (user is null)
            {
                await uow.WriteAuditLogAsync(null, "login_failed", "AppUser", null, null, false, ct);
                await uow.SaveChangesAsync(ct);
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
            }

            var hasher = new PasswordHasher<AppUser>();
            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (verify == PasswordVerificationResult.Failed)
            {
                await uow.WriteAuditLogAsync(user.Id, "login_failed", "AppUser", user.Id.ToString(), null, false, ct);
                await uow.SaveChangesAsync(ct);
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
            }

            if (user.Status != UserStatus.Active || user.IsBlocked)
            {
                var message = user.Status switch
                {
                    UserStatus.PendingApproval => "Your account is waiting for approval.",
                    UserStatus.Suspended => "Your account is suspended.",
                    UserStatus.Blocked => "Your account is blocked.",
                    UserStatus.Rejected => "Your account was rejected.",
                    _ => "Your account is not active."
                };
                return Results.Json(new { error = message }, statusCode: 403);
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await uow.UpdateUserAsync(user, ct);
            await uow.WriteAuditLogAsync(user.Id, "login", "AppUser", user.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);

            var (token, expiresAt) = jwt.IssueToken(user);
            return Results.Ok(new
            {
                token,
                expiresAt,
                user = new { id = user.Id, email = user.Email, displayName = user.DisplayName, role = user.Role.ToString(), status = user.Status.ToString() }
            });
        });

        group.MapPost("/logout", async (HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext();
            if (caller is null)
                return Results.Unauthorized();

            await uow.WriteAuditLogAsync(caller.UserId, "logout", "AppUser", caller.UserId.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        }).RequireAuthorization();

        group.MapPost("/bootstrap-superadmin", async (BootstrapDto dto, IUnitOfWork uow, CancellationToken ct) =>
        {
            if (await uow.AnySuperAdminExistsAsync(ct))
                return Results.Json(new { error = "A Super Admin already exists. This endpoint is disabled." }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
                return Results.BadRequest(new { error = "Email and a password of at least 8 characters are required." });

            var email = dto.Email.Trim();
            if (await uow.GetUserByEmailAsync(email, ct) is not null)
                return Results.Conflict(new { error = "A user with this email already exists." });

            var hasher = new PasswordHasher<AppUser>();
            var user = new AppUser
            {
                Id = Guid.NewGuid(), Role = UserRole.SuperAdmin, Status = UserStatus.Active,
                ParentId = null, Email = email, DisplayName = dto.DisplayName.Trim(),
                CreditLimit = 9999999999999999.99m, CreditBalance = 0,
                IsBlocked = false, TokenVersion = 0,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                ApprovedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, dto.Password);
            await uow.AddUserAsync(user, ct);
            await uow.WriteAuditLogAsync(user.Id, "bootstrap_superadmin", "AppUser", user.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { userId = user.Id });
        });
    }
}

public record LoginDto(string Email, string Password);
public record BootstrapDto(string Email, string Password, string DisplayName);
