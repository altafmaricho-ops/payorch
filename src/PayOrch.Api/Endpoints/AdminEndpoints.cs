using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using PayOrch.Api.Auth;
using PayOrch.Application.Fraud;
using PayOrch.Application.Sellers;
using PayOrch.Application.Users;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization();

        // ============================================================
        // CREATE SUBORDINATE USER
        // ============================================================

        group.MapPost("/users",
            async (
                CreateUserDto dto,
                HttpRequest req,
                CreateSubordinateUserService svc,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(dto.Email) ||
                    string.IsNullOrWhiteSpace(dto.DisplayName) ||
                    string.IsNullOrWhiteSpace(dto.Password) ||
                    dto.Password.Length < 8)
                {
                    return Results.BadRequest(new
                    {
                        error = "Email, display name and a password of at least 8 characters are required."
                    });
                }

                var hasher = new PasswordHasher<AppUser>();

                var passwordHash = hasher.HashPassword(
                    new AppUser(),
                    dto.Password);

                var result = await svc.HandleAsync(
                    new CreateSubordinateUserCommand(
                        caller,
                        dto.Email.Trim(),
                        dto.DisplayName.Trim(),
                        passwordHash,
                        dto.InitialCreditLimit),
                    ct);

                return result.Success
                    ? Results.Ok(new
                    {
                        userId = result.UserId,
                        status = UserStatus.PendingApproval.ToString()
                    })
                    : Results.BadRequest(new
                    {
                        error = result.ErrorMessage
                    });
            });

        // ============================================================
        // CHANGE USER STATUS
        // approve / reject / suspend / block / activate
        // ============================================================

        group.MapPatch("/users/{userId:guid}/status",
            async (
                Guid userId,
                ChangeUserStatusDto dto,
                HttpRequest req,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                var target = await uow.GetUserByIdAsync(userId, ct);

                if (target is null)
                    return Results.NotFound();

                // Never allow an administrator to modify itself
                // or the SuperAdmin through this endpoint.
                if (target.Role == UserRole.SuperAdmin ||
                    target.Id == caller.UserId ||
                    !await uow.IsUserInCallerScopeAsync(
                        caller,
                        target.Id,
                        ct))
                {
                    return Results.Forbid();
                }

                // Admins may manage SubAdmins only.
                if (caller.Role == UserRole.Admin &&
                    target.Role != UserRole.SubAdmin)
                {
                    return Results.Forbid();
                }

                // SubAdmins cannot manage users.
                if (caller.Role == UserRole.SubAdmin)
                    return Results.Forbid();

                if (string.IsNullOrWhiteSpace(dto.Action))
                {
                    return Results.BadRequest(new
                    {
                        error = "Action is required."
                    });
                }

                var action = dto.Action.Trim().ToLowerInvariant();

                var valid = action switch
                {
                    "approve"
                        when target.Status is
                            UserStatus.PendingApproval or
                            UserStatus.Rejected
                        => true,

                    "reject"
                        when target.Status == UserStatus.PendingApproval
                        => true,

                    "suspend"
                        when target.Status == UserStatus.Active
                        => true,

                    "block"
                        when target.Status is
                            UserStatus.Active or
                            UserStatus.Suspended
                        => true,

                    "activate"
                        when target.Status is
                            UserStatus.Suspended or
                            UserStatus.Blocked
                        => true,

                    _ => false
                };

                if (!valid)
                {
                    return Results.BadRequest(new
                    {
                        error =
                            $"Cannot apply '{action}' to account status '{target.Status}'."
                    });
                }

                target.Status = action switch
                {
                    "approve" => UserStatus.Active,
                    "reject" => UserStatus.Rejected,
                    "suspend" => UserStatus.Suspended,
                    "block" => UserStatus.Blocked,
                    "activate" => UserStatus.Active,
                    _ => target.Status
                };

                target.IsBlocked =
                    target.Status == UserStatus.Blocked;

                target.StatusReason =
                    string.IsNullOrWhiteSpace(dto.Reason)
                        ? null
                        : dto.Reason.Trim();

                if (action is "approve" or "activate")
                {
                    target.ApprovedBy = caller.UserId;
                    target.ApprovedAt = DateTimeOffset.UtcNow;
                }

                // Revoke all previously issued tokens/sessions.
                target.TokenVersion++;

                target.UpdatedAt = DateTimeOffset.UtcNow;

                await uow.UpdateUserAsync(target, ct);

                await uow.WriteAuditLogAsync(
                    caller.UserId,
                    $"user_{action}",
                    "AppUser",
                    target.Id.ToString(),
                    JsonSerializer.Serialize(new
                    {
                        dto.Reason,
                        oldStatus = target.Status.ToString(),
                        newStatus = target.Status.ToString()
                    }),
                    true,
                    ct);

                await uow.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    userId,
                    status = target.Status.ToString(),
                    isBlocked = target.IsBlocked
                });
            });

        // ============================================================
        // RESET PASSWORD
        // ============================================================

        group.MapPost("/users/{userId:guid}/reset-password",
            async (
                Guid userId,
                ResetPasswordDto dto,
                HttpRequest req,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (caller.Role == UserRole.SubAdmin ||
                    caller.UserId == userId ||
                    !await uow.IsUserInCallerScopeAsync(
                        caller,
                        userId,
                        ct))
                {
                    return Results.Forbid();
                }

                var target = await uow.GetUserByIdAsync(
                    userId,
                    ct);

                if (target is null ||
                    target.Role == UserRole.SuperAdmin)
                {
                    return Results.NotFound();
                }

                if (string.IsNullOrWhiteSpace(dto.NewPassword) ||
                    dto.NewPassword.Length < 8)
                {
                    return Results.BadRequest(new
                    {
                        error = "Password must contain at least 8 characters."
                    });
                }

                var hasher = new PasswordHasher<AppUser>();

                target.PasswordHash =
                    hasher.HashPassword(
                        target,
                        dto.NewPassword);

                // Revoke all existing sessions.
                target.TokenVersion++;

                target.UpdatedAt =
                    DateTimeOffset.UtcNow;

                await uow.UpdateUserAsync(
                    target,
                    ct);

                await uow.WriteAuditLogAsync(
                    caller.UserId,
                    "reset_password",
                    "AppUser",
                    target.Id.ToString(),
                    null,
                    true,
                    ct);

                await uow.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    userId,
                    message =
                        "Password reset. Existing sessions have been revoked."
                });
            });

        // ============================================================
        // ADJUST USER CREDIT
        // ============================================================

        group.MapPost("/users/{targetUserId:guid}/credit",
            async (
                Guid targetUserId,
                AdjustCreditDto dto,
                HttpRequest req,
                AdjustCreditService svc,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                var result = await svc.HandleAsync(
                    new AdjustCreditCommand(
                        caller,
                        targetUserId,
                        dto.Delta,
                        dto.Reason),
                    ct);

                return result.Success
                    ? Results.Ok(new
                    {
                        newBalance = result.NewBalance
                    })
                    : Results.BadRequest(new
                    {
                        error = result.ErrorMessage
                    });
            });

        // ============================================================
        // ONBOARD SELLER
        // ============================================================

        group.MapPost("/tenants/{tenantId:guid}/sellers",
            async (
                Guid tenantId,
                OnboardSellerDto dto,
                HttpRequest req,
                OnboardSellerService svc,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (caller.Role == UserRole.SubAdmin ||
                    !await uow.IsTenantInCallerScopeAsync(
                        caller,
                        tenantId,
                        ct))
                {
                    return Results.Forbid();
                }

                if (dto.CommissionPercent is < 0 or > 100)
                {
                    return Results.BadRequest(new
                    {
                        error = "Commission must be between 0 and 100."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.BusinessName))
                {
                    return Results.BadRequest(new
                    {
                        error = "Business name is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.ContactPhone))
                {
                    return Results.BadRequest(new
                    {
                        error = "Contact phone is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.ContactEmail))
                {
                    return Results.BadRequest(new
                    {
                        error = "Contact email is required."
                    });
                }

                var result = await svc.HandleAsync(
                    new OnboardSellerCommand(
                        tenantId,
                        caller.UserId,
                        dto.BusinessName.Trim(),
                        dto.ContactPhone.Trim(),
                        dto.ContactEmail.Trim(),
                        dto.UpiCollectionApp,
                        dto.CommissionPercent),
                    ct);

                return result.Success
                    ? Results.Ok(new
                    {
                        sellerId = result.SellerId,
                        sellerCode = result.SellerCode,
                        status = result.Status?.ToString(),
                        warning = result.ErrorMessage
                    })
                    : Results.BadRequest(new
                    {
                        error = result.ErrorMessage
                    });
            });

        // ============================================================
        // LIST SELLERS
        // ============================================================

        group.MapGet("/tenants/{tenantId:guid}/sellers",
            async (
                Guid tenantId,
                HttpRequest req,
                ListSellersService svc,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (!await uow.IsTenantInCallerScopeAsync(
                        caller,
                        tenantId,
                        ct))
                {
                    return Results.Forbid();
                }

                return Results.Ok(
                    await svc.HandleAsync(
                        caller,
                        tenantId,
                        ct));
            });

        // ============================================================
        // CHANGE SELLER STATUS
        // reject / suspend / activate
        // ============================================================

        group.MapPatch(
            "/tenants/{tenantId:guid}/sellers/{sellerId:guid}/status",
            async (
                Guid tenantId,
                Guid sellerId,
                ChangeSellerStatusDto dto,
                HttpRequest req,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (caller.Role == UserRole.SubAdmin ||
                    !await uow.IsTenantInCallerScopeAsync(
                        caller,
                        tenantId,
                        ct))
                {
                    return Results.Forbid();
                }

                var seller = await uow.GetSellerByIdAsync(
                    sellerId,
                    ct);

                if (seller is null ||
                    seller.TenantId != tenantId)
                {
                    return Results.NotFound();
                }

                if (string.IsNullOrWhiteSpace(dto.Action))
                {
                    return Results.BadRequest(new
                    {
                        error = "Action is required."
                    });
                }

                var action =
                    dto.Action.Trim().ToLowerInvariant();

                var valid = action switch
                {
                    "reject"
                        when seller.Status is
                            SellerOnboardingStatus.PendingReview or
                            SellerOnboardingStatus.SubmittedToPsp
                        => true,

                    "suspend"
                        when seller.Status ==
                            SellerOnboardingStatus.Active
                        => true,

                    "activate"
                        when seller.Status is
                            SellerOnboardingStatus.Suspended or
                            SellerOnboardingStatus.Rejected
                        => seller.PspLinkedAccountId is not null,

                    _ => false
                };

                if (!valid)
                {
                    return Results.BadRequest(new
                    {
                        error =
                            $"Cannot apply '{action}' to seller status '{seller.Status}'."
                    });
                }

                seller.Status = action switch
                {
                    "reject" =>
                        SellerOnboardingStatus.Rejected,

                    "suspend" =>
                        SellerOnboardingStatus.Suspended,

                    "activate" =>
                        SellerOnboardingStatus.Active,

                    _ => seller.Status
                };

                seller.StatusReason =
                    string.IsNullOrWhiteSpace(dto.Reason)
                        ? null
                        : dto.Reason.Trim();

                seller.UpdatedAt =
                    DateTimeOffset.UtcNow;

                await uow.UpdateSellerAsync(
                    seller,
                    ct);

                await uow.WriteAuditLogAsync(
                    caller.UserId,
                    $"seller_{action}",
                    "Seller",
                    seller.Id.ToString(),
                    JsonSerializer.Serialize(new
                    {
                        dto.Reason
                    }),
                    true,
                    ct);

                await uow.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    sellerId,
                    status = seller.Status.ToString()
                });
            });

        // ============================================================
        // EXPORT SELLERS
        // ============================================================

        group.MapGet(
            "/tenants/{tenantId:guid}/sellers/export",
            async (
                Guid tenantId,
                HttpRequest req,
                ExportGuardService guard,
                IUnitOfWork uow,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (!await uow.IsTenantInCallerScopeAsync(
                        caller,
                        tenantId,
                        ct))
                {
                    return Results.Forbid();
                }

                var allowed =
                    await guard.CheckAndAuditAsync(
                        caller,
                        "sellers_csv",
                        ct);

                if (!allowed)
                {
                    return Results.StatusCode(
                        StatusCodes.Status403Forbidden);
                }

                var sellers =
                    await uow.ListSellersForTenantAsync(
                        tenantId,
                        ct);

                var sb = new StringBuilder(
                    "SellerId,SellerCode,BusinessName,ContactPhone,ContactEmail,Status,CommissionPercent,CreatedAt\n");

                foreach (var s in sellers)
                {
                    sb.AppendLine(
                        $"{s.Id}," +
                        $"{Csv(s.SellerCode)}," +
                        $"{Csv(s.BusinessName)}," +
                        $"{Csv(s.ContactPhone)}," +
                        $"{Csv(s.ContactEmail)}," +
                        $"{s.Status}," +
                        $"{s.CommissionPercent}," +
                        $"{s.CreatedAt:O}");
                }

                await uow.SaveChangesAsync(ct);

                return Results.File(
                    Encoding.UTF8.GetBytes(sb.ToString()),
                    "text/csv",
                    "payorch-sellers.csv");
            });

        // ============================================================
        // FRAUD / RED FLAG
        // ============================================================

        group.MapPost(
            "/fraud/red-flag",
            async (
                RedFlagDto dto,
                HttpRequest req,
                RedFlagService svc,
                CancellationToken ct) =>
            {
                var caller = req.TryGetCallerContext();

                if (caller is null)
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(dto.IdentityType))
                {
                    return Results.BadRequest(new
                    {
                        error = "Identity type is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.RawIdentityValue))
                {
                    return Results.BadRequest(new
                    {
                        error = "Identity value is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.Reason))
                {
                    return Results.BadRequest(new
                    {
                        error = "Reason is required."
                    });
                }

                if (dto.TemporaryHours is <= 0)
                {
                    return Results.BadRequest(new
                    {
                        error = "Temporary hours must be positive."
                    });
                }

                // IMPORTANT:
                // Explicitly declare nullable TimeSpan so the conditional
                // expression can resolve TimeSpan vs null correctly.
                TimeSpan? duration =
                    dto.TemporaryHours.HasValue
                        ? TimeSpan.FromHours(
                            dto.TemporaryHours.Value)
                        : null;

                var result = await svc.HandleAsync(
                    new RedFlagCommand(
                        caller,
                        dto.IdentityType.Trim().ToLowerInvariant(),
                        dto.RawIdentityValue.Trim(),
                        dto.Reason.Trim(),
                        duration),
                    ct);

                return result.Success
                    ? Results.Ok(new
                    {
                        message = "Identity red-flagged successfully."
                    })
                    : Results.BadRequest(new
                    {
                        error = result.ErrorMessage
                    });
            });
    }

    // ================================================================
    // CSV HELPER
    // ================================================================

    private static string Csv(string? value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }
}

// ====================================================================
// DTOs
// ====================================================================

public record CreateUserDto(
    string Email,
    string DisplayName,
    string Password,
    decimal InitialCreditLimit);

public record ChangeUserStatusDto(
    string Action,
    string? Reason);

public record AdjustCreditDto(
    decimal Delta,
    string Reason);

public record ResetPasswordDto(
    string NewPassword);

public record OnboardSellerDto(
    string BusinessName,
    string ContactPhone,
    string ContactEmail,
    string? UpiCollectionApp,
    decimal CommissionPercent);

public record ChangeSellerStatusDto(
    string Action,
    string? Reason);

public record RedFlagDto(
    string IdentityType,
    string RawIdentityValue,
    string Reason,
    int? TemporaryHours);