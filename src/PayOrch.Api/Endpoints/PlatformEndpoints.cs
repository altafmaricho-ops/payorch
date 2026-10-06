using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PayOrch.Api.Auth;
using PayOrch.Application.Common;
using PayOrch.Application.Psp;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Endpoints;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform").WithTags("Platform").RequireAuthorization();

        group.MapGet("/summary", async (Guid? tenantId, DateTimeOffset? from, DateTimeOffset? to, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (tenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(caller, tenantId.Value, ct)) return Results.Forbid();
            var metrics = await uow.GetPaymentMetricsAsync(caller, tenantId, from, to, ct);
            var tenants = await uow.ListTenantsForCallerAsync(caller, ct);
            var users = await uow.ListUsersForCallerAsync(caller, ct);
            var providers = await uow.ListPaymentProvidersAsync(caller, null, ct);
            var rules = await uow.ListRoutingRulesAsync(caller, null, ct);
            var orders = await uow.ListOrdersForCallerAsync(caller, tenantId, null, from, to, ct, 12);
            return Results.Ok(new
            {
                role = caller.Role.ToString(),
                collection = metrics.Total,
                successful = metrics.Successful,
                failed = metrics.Failed,
                pending = metrics.Pending,
                activeWebsites = tenants.Count(x => x.Status == TenantStatus.Active && x.IsActive),
                websites = tenants.Count,
                pendingWebsites = tenants.Count(x => x.Status == TenantStatus.PendingApproval),
                admins = users.Count(x => x.Role == UserRole.Admin),
                subAdmins = users.Count(x => x.Role == UserRole.SubAdmin),
                providerAccounts = providers.Count,
                activeRoutes = rules.Count(x => x.IsEnabled),
                recentPayments = orders.Take(12).Select(x => new
                {
                    id = x.Id, tenantId = x.TenantId, merchantOrderRef = x.MerchantOrderRef,
                    amount = x.Amount, currency = x.Currency, status = x.Status.ToString(),
                    psp = x.PspCode, customer = x.CustomerName, createdAt = x.CreatedAt
                })
            });
        });

        group.MapGet("/websites", async (HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            var rows = await uow.ListTenantsForCallerAsync(caller, ct);
            if (caller.Role == UserRole.SubAdmin)
                return Results.Ok(rows.Select(x => new
                {
                    id = x.Id, subAdminId = x.SubAdminId, merchantCode = x.MerchantCode,
                    displayName = x.MerchantCode, vertical = x.Vertical, callbackUrl = string.Empty,
                    commissionPercent = x.CommissionPercent, apiKey = string.Empty,
                    status = x.Status.ToString(), isActive = x.IsActive, createdAt = x.CreatedAt
                }));
            return Results.Ok(rows.Select(x => new
            {
                id = x.Id, subAdminId = x.SubAdminId, merchantCode = x.MerchantCode, displayName = x.DisplayName,
                vertical = x.Vertical, callbackUrl = x.CallbackUrl, commissionPercent = x.CommissionPercent,
                apiKey = x.ApiKey, status = x.Status.ToString(), isActive = x.IsActive, createdAt = x.CreatedAt
            }));
        });

        group.MapPost("/websites", async (CreateWebsiteDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(dto.MerchantCode) || string.IsNullOrWhiteSpace(dto.DisplayName) || string.IsNullOrWhiteSpace(dto.Vertical))
                return Results.BadRequest(new { error = "Merchant code, display name and vertical are required." });
            if (dto.CommissionPercent is < 0 or > 100)
                return Results.BadRequest(new { error = "Commission must be between 0 and 100." });
            if (!dto.SubAdminId.HasValue)
                return Results.BadRequest(new { error = "Select a SubAdmin owner for the website." });

            var owner = await uow.GetUserByIdAsync(dto.SubAdminId.Value, ct);
            if (owner is null || owner.Role != UserRole.SubAdmin || owner.Status != UserStatus.Active ||
                !await uow.IsUserInCallerScopeAsync(caller, owner.Id, ct))
                return Results.Forbid();

            var apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var callbackSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(), SubAdminId = owner.Id,
                MerchantCode = dto.MerchantCode.Trim(), DisplayName = dto.DisplayName.Trim(), Vertical = dto.Vertical.Trim(),
                CallbackUrl = dto.CallbackUrl?.Trim() ?? string.Empty, CommissionPercent = dto.CommissionPercent,
                ApiKey = $"pk_live_{Guid.NewGuid():N}", ApiSecretHash = Sha256Hex(apiSecret), CallbackSecret = callbackSecret,
                Status = caller.Role == UserRole.SuperAdmin ? TenantStatus.Active : TenantStatus.PendingApproval,
                IsActive = caller.Role == UserRole.SuperAdmin, ApprovedBy = caller.Role == UserRole.SuperAdmin ? caller.UserId : null,
                ApprovedAt = caller.Role == UserRole.SuperAdmin ? DateTimeOffset.UtcNow : null,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };

            await uow.AddTenantAsync(tenant, ct);
            await uow.AddUserTenantAccessAsync(new UserTenantAccess { UserId = owner.Id, TenantId = tenant.Id }, ct);
            await uow.WriteAuditLogAsync(caller.UserId, "create_website", "Tenant", tenant.Id.ToString(), JsonSerializer.Serialize(new { tenant.MerchantCode, owner = owner.Id }), true, ct);
            await uow.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                tenantId = tenant.Id, merchantCode = tenant.MerchantCode, apiKey = tenant.ApiKey,
                apiSecret, callbackSecret, status = tenant.Status.ToString(),
                warning = "Store these credentials securely. The API secret and callback secret are shown only at creation."
            });
        });

        group.MapPost("/websites/{tenantId:guid}/rotate-secrets", async (Guid tenantId, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin || !await uow.IsTenantInCallerScopeAsync(caller, tenantId, ct)) return Results.Forbid();
            var tenant = await uow.GetTenantByIdAsync(tenantId, ct); if (tenant is null) return Results.NotFound();
            var apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var callbackSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            tenant.ApiSecretHash = Sha256Hex(apiSecret); tenant.CallbackSecret = callbackSecret; tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await uow.UpdateTenantAsync(tenant, ct);
            await uow.WriteAuditLogAsync(caller.UserId, "rotate_website_secrets", "Tenant", tenant.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { tenantId, apiKey = tenant.ApiKey, apiSecret, callbackSecret });
        });

        group.MapPatch("/websites/{tenantId:guid}/status", async (Guid tenantId, ChangeStatusDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin || !await uow.IsTenantInCallerScopeAsync(caller, tenantId, ct)) return Results.Forbid();
            var tenant = await uow.GetTenantByIdAsync(tenantId, ct); if (tenant is null) return Results.NotFound();
            var action = NormalizeAction(dto.Action);
            var result = ApplyTenantStatus(tenant, action, caller.UserId, dto.Reason);
            if (!result.Success) return Results.BadRequest(new { error = result.Error });
            await uow.UpdateTenantAsync(tenant, ct);
            await uow.WriteAuditLogAsync(caller.UserId, $"website_{action}", "Tenant", tenant.Id.ToString(), JsonSerializer.Serialize(new { dto.Reason }), true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { tenantId = tenant.Id, status = tenant.Status.ToString(), isActive = tenant.IsActive });
        });

        group.MapGet("/providers", async (Guid? tenantId, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            if (tenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(caller, tenantId.Value, ct)) return Results.Forbid();
            var providers = await uow.ListPaymentProvidersAsync(caller, tenantId, ct);
            return Results.Ok(providers.Select(x => new
            { x.Id, x.OwnerUserId, x.TenantId, x.ProviderCode, x.DisplayName, x.AccountLabel, x.MerchantAccountRef, x.KeyId, x.IsActive, x.IsTestMode, x.HealthStatus, x.CreatedAt, x.UpdatedAt }));
        });

        group.MapPost("/providers", async (CreateProviderDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(dto.ProviderCode) || string.IsNullOrWhiteSpace(dto.AccountLabel))
                return Results.BadRequest(new { error = "Provider code and account label are required." });
            if (dto.TenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(caller, dto.TenantId.Value, ct)) return Results.Forbid();
            if (dto.OwnerUserId.HasValue && (!await uow.IsUserInCallerScopeAsync(caller, dto.OwnerUserId.Value, ct) || caller.Role != UserRole.SuperAdmin)) return Results.Forbid();

            var provider = new PaymentProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = dto.OwnerUserId ?? caller.UserId,
                TenantId = dto.TenantId,
                ProviderCode = dto.ProviderCode.Trim().ToLowerInvariant(),
                DisplayName = dto.DisplayName.Trim(), AccountLabel = dto.AccountLabel.Trim(),
                MerchantAccountRef = dto.MerchantAccountRef?.Trim(), KeyId = dto.KeyId?.Trim(),
                IsActive = dto.IsActive, IsTestMode = dto.IsTestMode, HealthStatus = "NotTested",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            await uow.AddPaymentProviderAsync(provider, ct);
            await uow.WriteAuditLogAsync(caller.UserId, "create_provider", "PaymentProvider", provider.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { providerId = provider.Id });
        });

        group.MapPatch("/providers/{providerId:guid}/status", async (Guid providerId, ChangeToggleDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            var provider = await uow.GetPaymentProviderAsync(providerId, ct); if (provider is null) return Results.NotFound();
            if (!await uow.IsUserInCallerScopeAsync(caller, provider.OwnerUserId, ct) && (!provider.TenantId.HasValue || !await uow.IsTenantInCallerScopeAsync(caller, provider.TenantId.Value, ct))) return Results.Forbid();
            provider.IsActive = dto.Enabled; provider.UpdatedAt = DateTimeOffset.UtcNow;
            await uow.WriteAuditLogAsync(caller.UserId, dto.Enabled ? "provider_activate" : "provider_disable", "PaymentProvider", provider.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { providerId, isActive = provider.IsActive });
        });

        group.MapPost("/providers/{providerId:guid}/health", async (Guid providerId, HttpRequest req, IUnitOfWork uow, PayOrch.Application.Psp.IPspAdapterResolver resolver, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            var provider = await uow.GetPaymentProviderAsync(providerId, ct); if (provider is null) return Results.NotFound();
            if (!await uow.IsUserInCallerScopeAsync(caller, provider.OwnerUserId, ct) && (!provider.TenantId.HasValue || !await uow.IsTenantInCallerScopeAsync(caller, provider.TenantId.Value, ct))) return Results.Forbid();
            try
            {
                var adapter = resolver.GetByCode(provider.ProviderCode);
                var healthy = await adapter.HealthCheckAsync(ct);
                provider.HealthStatus = healthy ? "Healthy" : "Unhealthy";
                provider.UpdatedAt = DateTimeOffset.UtcNow;
                await uow.WriteAuditLogAsync(caller.UserId, "provider_health_check", "PaymentProvider", provider.Id.ToString(), JsonSerializer.Serialize(new { healthy }), true, ct);
                await uow.SaveChangesAsync(ct);
                return Results.Ok(new { providerId, healthStatus = provider.HealthStatus });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapGet("/routing", async (Guid? tenantId, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            if (tenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(caller, tenantId.Value, ct)) return Results.Forbid();
            return Results.Ok(await uow.ListRoutingRulesAsync(caller, tenantId, ct));
        });

        group.MapPost("/routing", async (CreateRoutingDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            if (dto.MinAmount < 0 || (dto.MaxAmount.HasValue && dto.MaxAmount < dto.MinAmount) || dto.Priority < 1)
                return Results.BadRequest(new { error = "Invalid amount range or priority." });
            if (!await uow.IsTenantInCallerScopeAsync(caller, dto.TenantId, ct)) return Results.Forbid();
            var provider = await uow.GetPaymentProviderAsync(dto.PaymentProviderId, ct);
            if (provider is null || !provider.IsActive || (provider.TenantId.HasValue && provider.TenantId != dto.TenantId))
                return Results.BadRequest(new { error = "Primary provider is missing, inactive, or belongs to another website." });
            if (dto.FallbackProviderId.HasValue)
            {
                var fallback = await uow.GetPaymentProviderAsync(dto.FallbackProviderId.Value, ct);
                if (fallback is null || !fallback.IsActive || (fallback.TenantId.HasValue && fallback.TenantId != dto.TenantId))
                    return Results.BadRequest(new { error = "Fallback provider is missing, inactive, or belongs to another website." });
            }
            var rule = new RoutingRule
            {
                Id = Guid.NewGuid(), OwnerUserId = caller.UserId, TenantId = dto.TenantId,
                PaymentProviderId = dto.PaymentProviderId, MinAmount = dto.MinAmount, MaxAmount = dto.MaxAmount,
                Priority = dto.Priority, IsEnabled = dto.IsEnabled, FallbackProviderId = dto.FallbackProviderId,
                PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? null : dto.PaymentMethod.Trim().ToLowerInvariant(),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            await uow.AddRoutingRuleAsync(rule, ct);
            await uow.WriteAuditLogAsync(caller.UserId, "create_routing_rule", "RoutingRule", rule.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { ruleId = rule.Id });
        });

        group.MapPatch("/routing/{ruleId:guid}/status", async (Guid ruleId, ChangeToggleDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var caller = req.TryGetCallerContext(); if (caller is null) return Results.Unauthorized();
            if (caller.Role == UserRole.SubAdmin) return Results.Forbid();
            var rules = await uow.ListRoutingRulesAsync(caller, null, ct);
            var rule = rules.FirstOrDefault(x => x.Id == ruleId); if (rule is null) return Results.NotFound();
            rule.IsEnabled = dto.Enabled; rule.UpdatedAt = DateTimeOffset.UtcNow;
            await uow.UpdateRoutingRuleAsync(rule, ct);
            await uow.WriteAuditLogAsync(caller.UserId, dto.Enabled ? "routing_enable" : "routing_disable", "RoutingRule", rule.Id.ToString(), null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok(new { ruleId, isEnabled = rule.IsEnabled });
        });

        group.MapPost("/users/{userId:guid}/permissions", async (Guid userId, SetPermissionDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (c.Role == UserRole.SubAdmin || !await uow.IsUserInCallerScopeAsync(c, userId, ct)) return Results.Forbid();
            if (dto.TenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(c, dto.TenantId.Value, ct)) return Results.Forbid();
            var target = await uow.GetUserByIdAsync(userId, ct); if (target is null) return Results.NotFound();
            var permission = await uow.GetUserPermissionAsync(userId, dto.PermissionCode.Trim(), dto.TenantId, ct);
            if (permission is null)
            {
                permission = new UserPermission { Id = Guid.NewGuid(), UserId = userId, PermissionCode = dto.PermissionCode.Trim(), TenantId = dto.TenantId, Granted = dto.Granted };
                await uow.AddUserPermissionAsync(permission, ct);
            }
            else permission.Granted = dto.Granted;
            await uow.WriteAuditLogAsync(c.UserId, "set_permission", "UserPermission", permission.Id.ToString(), JsonSerializer.Serialize(dto), true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok();
        });

        group.MapPost("/users/{userId:guid}/tenant-access", async (Guid userId, SetTenantAccessDto dto, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (c.Role == UserRole.SubAdmin || !await uow.IsUserInCallerScopeAsync(c, userId, ct) || !await uow.IsTenantInCallerScopeAsync(c, dto.TenantId, ct)) return Results.Forbid();
            var target = await uow.GetUserByIdAsync(userId, ct); if (target is null) return Results.NotFound();
            if (!await uow.HasUserTenantAccessAsync(userId, dto.TenantId, ct))
                await uow.AddUserTenantAccessAsync(new UserTenantAccess { UserId = userId, TenantId = dto.TenantId }, ct);
            await uow.WriteAuditLogAsync(c.UserId, "grant_tenant_access", "UserTenantAccess", $"{userId}:{dto.TenantId}", null, true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Ok();
        });

        group.MapGet("/users", async (
    HttpRequest req,
    IUnitOfWork uow,
    CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext();

            if (c is null)
                return Results.Unauthorized();

            var users = await uow.ListUsersForCallerAsync(c, ct);

            var result = users.Select(x => new
            {
                id = x.Id,
                role = x.Role.ToString(),
                status = x.Status.ToString(),
                email = x.Email,
                displayName = x.DisplayName,
                parentId = x.ParentId,
                creditLimit = x.CreditLimit,
                creditBalance = x.CreditBalance,
                isBlocked = x.IsBlocked,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            });

            return Results.Ok(result);
        });

        group.MapGet("/payments", async (Guid? tenantId, string? status, DateTimeOffset? from, DateTimeOffset? to, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (tenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(c, tenantId.Value, ct)) return Results.Forbid();
            var orders = await uow.ListOrdersForCallerAsync(c, tenantId, status, from, to, ct);
            return Results.Ok(orders.Select(ToPayment));
        });

        group.MapGet("/payments/{orderId:guid}", async (Guid orderId, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            var o = await uow.GetOrderDetailForCallerAsync(c, orderId, ct);
            return o is null ? Results.NotFound() : Results.Ok(ToPayment(o));
        });

        group.MapPost("/payments/{orderId:guid}/refund", async (Guid orderId, CreateRefundDto dto, HttpRequest req, IUnitOfWork uow, PayOrch.Application.Psp.IPspAdapterResolver resolver, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (c.Role == UserRole.SubAdmin) return Results.Forbid();
            var order = await uow.GetOrderDetailForCallerAsync(c, orderId, ct);
            if (order is null) return Results.NotFound();
            if (order.Status != OrderStatus.Succeeded || string.IsNullOrWhiteSpace(order.PspPaymentId)) return Results.BadRequest(new { error = "Only successful payments with a PSP payment id can be refunded." });
            if (dto.Amount <= 0) return Results.BadRequest(new { error = "Refund amount must be positive." });
            var refunded = await uow.GetRefundedAmountAsync(order.Id, ct);
            if (refunded + dto.Amount > order.Amount) return Results.BadRequest(new { error = "Refund exceeds the remaining refundable amount." });
            IPspAdapter adapter;
            try { adapter = resolver.GetByCode(order.PspCode); } catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            var result = await adapter.CreateRefundAsync(new PspRefundRequest(order.PspPaymentId, dto.Amount, order.Currency, dto.Reason), ct);
            var refund = new Refund
            {
                Id = Guid.NewGuid(), OrderId = order.Id, RequestedBy = c.UserId, Amount = dto.Amount, Reason = dto.Reason?.Trim(),
                PspRefundId = result.PspRefundId, Status = result.Success ? "Processed" : "Failed",
                CreatedAt = DateTimeOffset.UtcNow, CompletedAt = result.Success ? DateTimeOffset.UtcNow : null
            };
            await uow.AddRefundAsync(refund, ct);
            await uow.WriteAuditLogAsync(c.UserId, result.Success ? "refund_created" : "refund_failed", "Refund", refund.Id.ToString(), JsonSerializer.Serialize(new { orderId, dto.Amount, dto.Reason, refund.PspRefundId }), result.Success, ct);
            await uow.SaveChangesAsync(ct);
            return result.Success ? Results.Ok(new { refundId = refund.Id, status = refund.Status, pspRefundId = refund.PspRefundId }) : Results.BadRequest(new { error = result.ErrorMessage ?? "Refund request failed.", refundId = refund.Id });
        });

        group.MapGet("/audit", async (int? take, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            return Results.Ok(await uow.ListAuditLogsAsync(c, take ?? 200, ct));
        });

        group.MapGet("/risk/blocked", async (int? take, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (c.Role == UserRole.SubAdmin) return Results.Forbid();
            return Results.Ok(await uow.ListBlockedIdentitiesAsync(take ?? 200, ct));
        });

        group.MapGet("/reports/payments.csv", async (Guid? tenantId, string? status, DateTimeOffset? from, DateTimeOffset? to, HttpRequest req, IUnitOfWork uow, CancellationToken ct) =>
        {
            var c = req.TryGetCallerContext(); if (c is null) return Results.Unauthorized();
            if (tenantId.HasValue && !await uow.IsTenantInCallerScopeAsync(c, tenantId.Value, ct)) return Results.Forbid();
            var rows = await uow.ListOrdersForCallerAsync(c, tenantId, status, from, to, ct);
            var sb = new StringBuilder("PaymentId,WebsiteId,MerchantOrderRef,Amount,Currency,Status,PSP,Customer,CreatedAt\n");
            foreach (var o in rows)
            {
                sb.AppendLine(string.Join(',', o.Id, o.TenantId, Csv(o.MerchantOrderRef), o.Amount, o.Currency, o.Status,
                    Csv(o.PspCode), Csv(o.CustomerName), o.CreatedAt.ToString("O")));
            }
            await uow.WriteAuditLogAsync(c.UserId, "payment_csv_export", "Export", "payments", JsonSerializer.Serialize(new { tenantId, status, from, to, count = rows.Count }), true, ct);
            await uow.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "payorch-payments.csv");
        });
    }

    private static object ToPayment(Order x) => new
    {
        id = x.Id, tenantId = x.TenantId, merchantOrderRef = x.MerchantOrderRef, amount = x.Amount,
        currency = x.Currency, status = x.Status.ToString(), psp = x.PspCode, customer = x.CustomerName,
        createdAt = x.CreatedAt, pspOrderId = x.PspOrderId, pspPaymentId = x.PspPaymentId,
        failureReason = x.FailureReason, completedAt = x.CompletedAt, sellerId = x.SellerId
    };

    private static string NormalizeAction(string action) => action.Trim().ToLowerInvariant();

    private static (bool Success, string? Error) ApplyTenantStatus(Tenant tenant, string action, Guid actorId, string? reason)
    {
        switch (action)
        {
            case "approve" when tenant.Status is TenantStatus.PendingApproval or TenantStatus.Rejected:
                tenant.Status = TenantStatus.Active; tenant.IsActive = true; tenant.ApprovedBy = actorId; tenant.ApprovedAt = DateTimeOffset.UtcNow; break;
            case "reject" when tenant.Status == TenantStatus.PendingApproval:
                tenant.Status = TenantStatus.Rejected; tenant.IsActive = false; break;
            case "suspend" when tenant.Status == TenantStatus.Active:
                tenant.Status = TenantStatus.Suspended; tenant.IsActive = false; break;
            case "block" when tenant.Status is TenantStatus.Active or TenantStatus.Suspended:
                tenant.Status = TenantStatus.Blocked; tenant.IsActive = false; break;
            case "activate" when tenant.Status is TenantStatus.Suspended or TenantStatus.Blocked:
                tenant.Status = TenantStatus.Active; tenant.IsActive = true; break;
            default: return (false, $"Cannot apply '{action}' to website status '{tenant.Status}'.");
        }
        tenant.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        return (true, null);
    }

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

public record CreateWebsiteDto(string MerchantCode, string DisplayName, string Vertical, string? CallbackUrl, decimal CommissionPercent, Guid? SubAdminId);
public record CreateProviderDto(string ProviderCode, string DisplayName, string AccountLabel, string? MerchantAccountRef, string? KeyId, Guid? TenantId, bool IsActive, bool IsTestMode, Guid? OwnerUserId);
public record CreateRoutingDto(Guid TenantId, Guid PaymentProviderId, decimal MinAmount, decimal? MaxAmount, int Priority, bool IsEnabled, Guid? FallbackProviderId, string? PaymentMethod);
public record SetPermissionDto(string PermissionCode, Guid? TenantId, bool Granted);
public record SetTenantAccessDto(Guid TenantId);
public record ChangeStatusDto(string Action, string? Reason);
public record ChangeToggleDto(bool Enabled);
public record CreateRefundDto(decimal Amount, string? Reason);
