using Microsoft.EntityFrameworkCore;
using Npgsql;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public EfUnitOfWork(AppDbContext db)
        => _db = db;

    // ============================================================
    // TENANTS
    // ============================================================

    public Task<Tenant?> GetTenantByApiKeyAsync(
        string apiKey,
        CancellationToken ct)
        => _db.Tenants
            .FirstOrDefaultAsync(
                t => t.ApiKey == apiKey,
                ct);

    public Task<Tenant?> GetTenantByIdAsync(
        Guid tenantId,
        CancellationToken ct)
        => _db.Tenants
            .FirstOrDefaultAsync(
                t => t.Id == tenantId,
                ct);

    public async Task<Wallet> GetOrCreateWalletAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var wallet =
            await _db.Wallets
                .FirstOrDefaultAsync(
                    w => w.TenantId == tenantId,
                    ct);

        if (wallet is not null)
            return wallet;

        wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Balance = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Wallets.Add(wallet);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (
            DbUpdateException ex)
            when (
                ex.InnerException is PostgresException
                {
                    SqlState:
                    PostgresErrorCodes.UniqueViolation
                })
        {
            _db.Entry(wallet).State =
                EntityState.Detached;

            wallet =
                await _db.Wallets
                    .FirstAsync(
                        w => w.TenantId == tenantId,
                        ct);
        }

        return wallet;
    }

    // ============================================================
    // ORDERS
    // ============================================================

    public Task AddOrderAsync(
        Order order,
        CancellationToken ct)
        => _db.Orders
            .AddAsync(order, ct)
            .AsTask();

    public Task<Order?> GetOrderByPspOrderIdAsync(
        string pspOrderId,
        CancellationToken ct)
        => _db.Orders
            .FirstOrDefaultAsync(
                o => o.PspOrderId == pspOrderId,
                ct);

    public Task<Order?> GetOrderByTenantAndMerchantRefAsync(
        Guid tenantId,
        string merchantOrderRef,
        CancellationToken ct)
        => _db.Orders
            .FirstOrDefaultAsync(
                o =>
                    o.TenantId == tenantId &&
                    o.MerchantOrderRef == merchantOrderRef,
                ct);

    public Task<Order?> GetOrderByIdAsync(
        Guid orderId,
        CancellationToken ct)
        => _db.Orders
            .FirstOrDefaultAsync(
                o => o.Id == orderId,
                ct);

    public Task UpdateOrderAsync(
        Order order,
        CancellationToken ct)
    {
        _db.Orders.Update(order);
        return Task.CompletedTask;
    }

    // ============================================================
    // WEBHOOKS
    // ============================================================

    public Task<bool> WebhookEventExistsAsync(
        string pspCode,
        string pspEventId,
        CancellationToken ct)
        => _db.WebhookEvents
            .AnyAsync(
                w =>
                    w.PspCode == pspCode &&
                    w.PspEventId == pspEventId,
                ct);

    public Task AddWebhookEventAsync(
        WebhookEvent evt,
        CancellationToken ct)
        => _db.WebhookEvents
            .AddAsync(evt, ct)
            .AsTask();

    public async Task<IReadOnlyList<WebhookEvent>>
        ClaimPendingWebhookEventsAsync(
            int batchSize,
            TimeSpan leaseTimeout,
            CancellationToken ct)
    {
        var leaseCutoff =
            DateTimeOffset.UtcNow -
            leaseTimeout;

        var claimedIds =
            await _db.Database
                .SqlQuery<Guid>($"""
                    WITH candidates AS (
                        SELECT id
                        FROM webhook_events
                        WHERE processed_at IS NULL
                          AND signature_valid = true
                          AND (
                              processing_started_at IS NULL
                              OR processing_started_at < {leaseCutoff}
                          )
                        ORDER BY received_at
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE webhook_events w
                    SET processing_started_at = now()
                    FROM candidates c
                    WHERE w.id = c.id
                    RETURNING w.id
                    """)
                .ToListAsync(ct);

        if (claimedIds.Count == 0)
            return Array.Empty<WebhookEvent>();

        return await _db.WebhookEvents
            .Where(w => claimedIds.Contains(w.Id))
            .ToListAsync(ct);
    }

    public async Task MarkWebhookEventProcessedAsync(
        Guid webhookEventId,
        Guid? orderId,
        CancellationToken ct)
    {
        var evt =
            await _db.WebhookEvents
                .FirstAsync(
                    w => w.Id == webhookEventId,
                    ct);

        evt.ProcessedAt =
            DateTimeOffset.UtcNow;

        evt.OrderId = orderId;
    }

    // ============================================================
    // SELLERS
    // ============================================================

    public Task<Seller?> GetSellerByIdAsync(
        Guid sellerId,
        CancellationToken ct)
        => _db.Sellers
            .FirstOrDefaultAsync(
                s => s.Id == sellerId,
                ct);

    public Task<bool> SellerTransferExistsAsync(
        Guid orderId,
        Guid sellerId,
        CancellationToken ct)
        => _db.SellerTransfers
            .AnyAsync(
                x =>
                    x.OrderId == orderId &&
                    x.SellerId == sellerId,
                ct);

    public Task AddSellerTransferAsync(
        SellerTransfer transfer,
        CancellationToken ct)
        => _db.SellerTransfers
            .AddAsync(transfer, ct)
            .AsTask();

    public async Task<string> GenerateNextSellerCodeAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var count =
            await _db.Sellers
                .CountAsync(
                    s => s.TenantId == tenantId,
                    ct);

        return (count + 1).ToString("D3");
    }

    public Task AddSellerAsync(
        Seller seller,
        CancellationToken ct)
        => _db.Sellers
            .AddAsync(seller, ct)
            .AsTask();

    public async Task<IReadOnlyList<Seller>>
        ListSellersForTenantAsync(
            Guid tenantId,
            CancellationToken ct)
        => await _db.Sellers
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.SellerCode)
            .ToListAsync(ct);

    public Task UpdateSellerAsync(
        Seller seller,
        CancellationToken ct)
    {
        _db.Sellers.Update(seller);
        return Task.CompletedTask;
    }

    // ============================================================
    // USERS
    // ============================================================

    public Task<AppUser?> GetUserByIdAsync(
        Guid userId,
        CancellationToken ct)
        => _db.Users
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                ct);

    public Task<AppUser?> GetUserByEmailAsync(
        string email,
        CancellationToken ct)
        => _db.Users
            .FirstOrDefaultAsync(
                u => u.Email == email,
                ct);

    public Task<bool> AnySuperAdminExistsAsync(
        CancellationToken ct)
        => _db.Users
            .AnyAsync(
                u => u.Role == UserRole.SuperAdmin,
                ct);

    public async Task<IReadOnlyList<AppUser>>
        ListChildUsersAsync(
            Guid parentUserId,
            CancellationToken ct)
        => await _db.Users
            .Where(u => u.ParentId == parentUserId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

    public Task AddUserAsync(
        AppUser user,
        CancellationToken ct)
        => _db.Users
            .AddAsync(user, ct)
            .AsTask();

    public Task UpdateUserAsync(
        AppUser user,
        CancellationToken ct)
    {
        _db.Users.Update(user);
        return Task.CompletedTask;
    }

    // ============================================================
    // AUDIT
    // ============================================================

    public Task WriteAuditLogAsync(
        Guid? actorId,
        string action,
        string? targetType,
        string? targetId,
        string? metadataJson,
        bool allowed,
        CancellationToken ct)
        => _db.AuditLog
            .AddAsync(
                new AuditLogEntry
                {
                    ActorId = actorId,
                    Action = action,
                    TargetType = targetType,
                    TargetId = targetId,
                    MetadataJson = metadataJson,
                    Allowed = allowed,
                    CreatedAt =
                        DateTimeOffset.UtcNow
                },
                ct)
            .AsTask();

    public async Task<IReadOnlyList<AuditLogEntry>>
        ListAuditLogsAsync(
            CallerContext caller,
            int take,
            CancellationToken ct)
    {
        var ids =
            await ScopeUserIdsAsync(
                caller,
                ct);

        var q =
            _db.AuditLog
                .AsNoTracking()
                .Where(
                    x =>
                        x.ActorId == null ||
                        ids.Contains(x.ActorId.Value));

        return await q
            .OrderByDescending(
                x => x.CreatedAt)
            .Take(
                Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
    }

    // ============================================================
    // FRAUD
    // ============================================================

    public Task<BlockedIdentity?>
        GetBlockedIdentityAsync(
            string type,
            string hash,
            CancellationToken ct)
        => _db.BlockedIdentities
            .FirstOrDefaultAsync(
                x =>
                    x.IdentityType == type &&
                    x.IdentityHash == hash,
                ct);

    public Task AddBlockedIdentityAsync(
        BlockedIdentity identity,
        CancellationToken ct)
        => _db.BlockedIdentities
            .AddAsync(identity, ct)
            .AsTask();

    public async Task<bool>
        IsAnyIdentityBlockedAsync(
            IEnumerable<(string Type, string Hash)> identities,
            CancellationToken ct)
    {
        var now =
            DateTimeOffset.UtcNow;

        foreach (var (type, hash) in identities)
        {
            if (
                await _db.BlockedIdentities
                    .AnyAsync(
                        b =>
                            b.IdentityType == type &&
                            b.IdentityHash == hash &&
                            (
                                b.ExpiresAt == null ||
                                b.ExpiresAt > now
                            ),
                        ct))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<BlockedIdentity>>
        ListBlockedIdentitiesAsync(
            int take,
            CancellationToken ct)
        => await _db.BlockedIdentities
            .AsNoTracking()
            .OrderByDescending(
                x => x.FlaggedAt)
            .Take(
                Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

    // ============================================================
    // LEDGER
    // ============================================================

    public Task<bool> LedgerEntryExistsAsync(
        string refType,
        Guid refId,
        CancellationToken ct)
        => _db.LedgerEntries
            .AnyAsync(
                x =>
                    x.RefType == refType &&
                    x.RefId == refId,
                ct);

    public async Task ApplyLedgerEntryAsync(
        Guid walletId,
        LedgerDirection direction,
        decimal amount,
        string refType,
        Guid refId,
        CancellationToken ct)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount));

        await using var tx =
            await _db.Database
                .BeginTransactionAsync(ct);

        var wallet =
            await _db.Wallets
                .FromSqlInterpolated(
                    $"SELECT * FROM wallets WHERE id = {walletId} FOR UPDATE")
                .SingleAsync(ct);

        var signedAmount =
            direction == LedgerDirection.Credit
                ? amount
                : -amount;

        wallet.Balance += signedAmount;
        wallet.UpdatedAt =
            DateTimeOffset.UtcNow;

        _db.LedgerEntries.Add(
            new LedgerEntry
            {
                WalletId = walletId,
                Direction = direction,
                Amount = amount,
                RefType = refType,
                RefId = refId,
                BalanceAfter = wallet.Balance,
                CreatedAt =
                    DateTimeOffset.UtcNow
            });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ============================================================
    // CALLBACK
    // ============================================================

    public Task<bool> CallbackRelayExistsAsync(
        Guid orderId,
        string outcome,
        CancellationToken ct)
        => _db.CallbackRelays
            .AnyAsync(
                x => x.OrderId == orderId,
                ct);

    public Task EnqueueCallbackRelayAsync(
        CallbackRelay relay,
        CancellationToken ct)
        => _db.CallbackRelays
            .AddAsync(relay, ct)
            .AsTask();

    // ============================================================
    // PAYMENT PROVIDERS
    // ============================================================

    public Task<PaymentProvider?>
        GetPaymentProviderAsync(
            Guid id,
            CancellationToken ct)
        => _db.PaymentProviders
            .FirstOrDefaultAsync(
                x => x.Id == id,
                ct);

    public async Task<decimal>
        GetRefundedAmountAsync(
            Guid orderId,
            CancellationToken ct)
        => await _db.Refunds
            .Where(
                x =>
                    x.OrderId == orderId &&
                    (
                        x.Status == "Processed" ||
                        x.Status == "Pending"
                    ))
            .SumAsync(
                x => x.Amount,
                ct);

    public Task AddRefundAsync(
        Refund refund,
        CancellationToken ct)
        => _db.Refunds
            .AddAsync(refund, ct)
            .AsTask();

    public Task<PaymentProvider?>
        GetDefaultPaymentProviderAsync(
            Guid tenantId,
            CancellationToken ct)
        => _db.PaymentProviders
            .Where(
                x =>
                    (
                        x.TenantId == tenantId ||
                        x.TenantId == null
                    ) &&
                    x.IsActive)
            .OrderByDescending(
                x => x.TenantId == tenantId)
            .ThenByDescending(
                x => x.IsTestMode == false)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<RoutingRule?>
        FindRoutingRuleAsync(
            Guid tenantId,
            decimal amount,
            string? paymentMethod,
            CancellationToken ct)
        => _db.RoutingRules
            .Where(
                r =>
                    r.TenantId == tenantId &&
                    r.IsEnabled &&
                    r.MinAmount <= amount &&
                    (
                        r.MaxAmount == null ||
                        r.MaxAmount >= amount
                    ) &&
                    (
                        r.PaymentMethod == null ||
                        paymentMethod == null ||
                        r.PaymentMethod == paymentMethod
                    ))
            .OrderBy(r => r.Priority)
            .ThenByDescending(
                r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddPaymentProviderAsync(
        PaymentProvider provider,
        CancellationToken ct)
        => _db.PaymentProviders
            .AddAsync(provider, ct)
            .AsTask();

    // ============================================================
    // USER SCOPE
    // ============================================================

    private async Task<List<Guid>>
        ScopeUserIdsAsync(
            CallerContext caller,
            CancellationToken ct)
    {
        // SuperAdmin sees the complete platform.
        if (caller.Role == UserRole.SuperAdmin)
        {
            return await _db.Users
                .Select(x => x.Id)
                .ToListAsync(ct);
        }

        var ids =
            new HashSet<Guid>
            {
                caller.UserId
            };

        var queue =
            new Queue<Guid>();

        queue.Enqueue(caller.UserId);

        while (queue.Count > 0)
        {
            var parent =
                queue.Dequeue();

            var children =
                await _db.Users
                    .Where(
                        x =>
                            x.ParentId == parent)
                    .Select(
                        x => x.Id)
                    .ToListAsync(ct);

            foreach (var childId in children)
            {
                if (!ids.Add(childId))
                    continue;

                queue.Enqueue(childId);
            }
        }

        return ids.ToList();
    }

    public async Task<bool>
        IsUserInCallerScopeAsync(
            CallerContext caller,
            Guid userId,
            CancellationToken ct)
    {
        if (caller.Role ==
            UserRole.SuperAdmin)
        {
            return true;
        }

        var ids =
            await ScopeUserIdsAsync(
                caller,
                ct);

        return ids.Contains(userId);
    }

    // ============================================================
    // PAYMENT PROVIDERS - SCOPED
    // ============================================================

    public async Task<IReadOnlyList<PaymentProvider>>
        ListPaymentProvidersAsync(
            CallerContext caller,
            Guid? tenantId,
            CancellationToken ct)
    {
        var userIds =
            await ScopeUserIdsAsync(
                caller,
                ct);

        var tenantIds =
            (
                await ListTenantsForCallerAsync(
                    caller,
                    ct)
            )
            .Select(x => x.Id)
            .ToList();

        var superAdminIds =
            await _db.Users
                .Where(
                    x =>
                        x.Role ==
                        UserRole.SuperAdmin)
                .Select(x => x.Id)
                .ToListAsync(ct);

        var q =
            _db.PaymentProviders
                .Where(
                    x =>
                        userIds.Contains(
                            x.OwnerUserId) ||
                        (
                            x.TenantId.HasValue &&
                            tenantIds.Contains(
                                x.TenantId.Value)
                        ) ||
                        (
                            !x.TenantId.HasValue &&
                            superAdminIds.Contains(
                                x.OwnerUserId)
                        ));

        if (tenantId.HasValue)
        {
            q = q.Where(
                x =>
                    x.TenantId ==
                    tenantId.Value);
        }

        return await q
            .OrderByDescending(
                x => x.IsActive)
            .ThenBy(
                x => x.DisplayName)
            .ToListAsync(ct);
    }

    // ============================================================
    // ROUTING
    // ============================================================

    public async Task<IReadOnlyList<RoutingRule>>
        ListRoutingRulesAsync(
            CallerContext caller,
            Guid? tenantId,
            CancellationToken ct)
    {
        var userIds =
            await ScopeUserIdsAsync(
                caller,
                ct);

        var tenantIds =
            (
                await ListTenantsForCallerAsync(
                    caller,
                    ct)
            )
            .Select(x => x.Id)
            .ToList();

        var q =
            _db.RoutingRules
                .Where(
                    x =>
                        userIds.Contains(
                            x.OwnerUserId) ||
                        (
                            x.TenantId.HasValue &&
                            tenantIds.Contains(
                                x.TenantId.Value)
                        ));

        if (tenantId.HasValue)
        {
            q = q.Where(
                x =>
                    x.TenantId ==
                    tenantId.Value);
        }

        return await q
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.MinAmount)
            .ToListAsync(ct);
    }

    public Task AddRoutingRuleAsync(
        RoutingRule rule,
        CancellationToken ct)
        => _db.RoutingRules
            .AddAsync(rule, ct)
            .AsTask();

    public Task UpdateRoutingRuleAsync(
        RoutingRule rule,
        CancellationToken ct)
    {
        _db.RoutingRules.Update(rule);
        return Task.CompletedTask;
    }

    // ============================================================
    // TENANT MANAGEMENT
    // ============================================================

    public Task AddTenantAsync(
        Tenant tenant,
        CancellationToken ct)
        => _db.Tenants
            .AddAsync(tenant, ct)
            .AsTask();

    public Task UpdateTenantAsync(
        Tenant tenant,
        CancellationToken ct)
    {
        _db.Tenants.Update(tenant);
        return Task.CompletedTask;
    }

    // ============================================================
    // PERMISSIONS
    // ============================================================

    public Task<UserPermission?>
        GetUserPermissionAsync(
            Guid userId,
            string permissionCode,
            Guid? tenantId,
            CancellationToken ct)
        => _db.UserPermissions
            .FirstOrDefaultAsync(
                x =>
                    x.UserId == userId &&
                    x.PermissionCode ==
                    permissionCode &&
                    x.TenantId == tenantId,
                ct);

    public Task AddUserPermissionAsync(
        UserPermission permission,
        CancellationToken ct)
        => _db.UserPermissions
            .AddAsync(permission, ct)
            .AsTask();

    public Task<bool>
        HasUserTenantAccessAsync(
            Guid userId,
            Guid tenantId,
            CancellationToken ct)
        => _db.UserTenantAccess
            .AnyAsync(
                x =>
                    x.UserId == userId &&
                    x.TenantId == tenantId,
                ct);

    public Task AddUserTenantAccessAsync(
        UserTenantAccess access,
        CancellationToken ct)
        => _db.UserTenantAccess
            .AddAsync(access, ct)
            .AsTask();

    // ============================================================
    // TENANTS VISIBLE TO CALLER
    // ============================================================

    public async Task<IReadOnlyList<Tenant>>
        ListTenantsForCallerAsync(
            CallerContext caller,
            CancellationToken ct)
    {
        if (caller.Role ==
            UserRole.SuperAdmin)
        {
            return await _db.Tenants
                .OrderBy(x => x.MerchantCode)
                .ToListAsync(ct);
        }

        var ids =
            await ScopeUserIdsAsync(
                caller,
                ct);

        var explicitIds =
            await _db.UserTenantAccess
                .Where(
                    x =>
                        x.UserId ==
                        caller.UserId)
                .Select(
                    x => x.TenantId)
                .ToListAsync(ct);

        return await _db.Tenants
            .Where(
                x =>
                    ids.Contains(
                        x.SubAdminId) ||
                    explicitIds.Contains(
                        x.Id))
            .OrderBy(
                x => x.MerchantCode)
            .ToListAsync(ct);
    }

    public async Task<bool>
        IsTenantInCallerScopeAsync(
            CallerContext caller,
            Guid tenantId,
            CancellationToken ct)
        => (
            await ListTenantsForCallerAsync(
                caller,
                ct)
        ).Any(
            x => x.Id == tenantId);

    // ============================================================
    // USERS VISIBLE TO CALLER
    //
    // IMPORTANT:
    // Admin -> own SubAdmins
    // SuperAdmin -> all users
    // SubAdmin -> itself + descendants (normally none)
    // ============================================================

    public async Task<IReadOnlyList<AppUser>>
        ListUsersForCallerAsync(
            CallerContext caller,
            CancellationToken ct)
    {
        if (caller.Role ==
            UserRole.SuperAdmin)
        {
            return await _db.Users
                .OrderBy(x => x.Role)
                .ThenBy(x => x.DisplayName)
                .ToListAsync(ct);
        }

        if (caller.Role ==
            UserRole.Admin)
        {
            return await _db.Users
                .Where(
                    x =>
                        x.ParentId ==
                        caller.UserId)
                .OrderBy(
                    x => x.Role)
                .ThenBy(
                    x => x.DisplayName)
                .ToListAsync(ct);
        }

        // SubAdmin should only see itself.
        return await _db.Users
            .Where(
                x =>
                    x.Id ==
                    caller.UserId)
            .OrderBy(
                x => x.DisplayName)
            .ToListAsync(ct);
    }

    // ============================================================
    // ORDERS
    // ============================================================

    public async Task<IReadOnlyList<Order>>
        ListOrdersForCallerAsync(
            CallerContext caller,
            Guid? tenantId,
            string? status,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct,
            int take = 500)
    {
        var tenants =
            await ListTenantsForCallerAsync(
                caller,
                ct);

        var tenantIds =
            tenants
                .Select(x => x.Id)
                .ToList();

        var q =
            _db.Orders
                .Where(
                    x =>
                        tenantIds.Contains(
                            x.TenantId));

        if (tenantId.HasValue)
        {
            q = q.Where(
                x =>
                    x.TenantId ==
                    tenantId.Value);
        }

        if (
            !string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OrderStatus>(
                status,
                true,
                out var parsed))
        {
            q = q.Where(
                x =>
                    x.Status ==
                    parsed);
        }

        if (from.HasValue)
        {
            q = q.Where(
                x =>
                    x.CreatedAt >=
                    from.Value);
        }

        if (to.HasValue)
        {
            q = q.Where(
                x =>
                    x.CreatedAt <=
                    to.Value);
        }

        return await q
            .OrderByDescending(
                x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 10000))
            .ToListAsync(ct);
    }

    public async Task<Order?>
        GetOrderDetailForCallerAsync(
            CallerContext caller,
            Guid orderId,
            CancellationToken ct)
    {
        var order =
            await _db.Orders
                .FirstOrDefaultAsync(
                    x => x.Id == orderId,
                    ct);

        if (order is null)
            return null;

        return await IsTenantInCallerScopeAsync(
            caller,
            order.TenantId,
            ct)
            ? order
            : null;
    }

    public async Task<int>
        CountActiveTenantsForCallerAsync(
            CallerContext caller,
            CancellationToken ct)
        => (
            await ListTenantsForCallerAsync(
                caller,
                ct)
        )
        .Count(
            x =>
                x.Status ==
                TenantStatus.Active &&
                x.IsActive);

    // ============================================================
    // PAYMENT METRICS
    // ============================================================

    public async Task<(
        decimal Total,
        int Successful,
        int Failed,
        int Pending)>
        GetPaymentMetricsAsync(
            CallerContext caller,
            Guid? tenantId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct)
    {
        var tenants =
            await ListTenantsForCallerAsync(
                caller,
                ct);

        var ids =
            tenants
                .Select(x => x.Id)
                .ToList();

        var q =
            _db.Orders
                .Where(
                    x =>
                        ids.Contains(
                            x.TenantId));

        if (tenantId.HasValue)
            q = q.Where(x => x.TenantId == tenantId.Value);

        if (from.HasValue)
        {
            q = q.Where(
                x =>
                    x.CreatedAt >=
                    from.Value);
        }

        if (to.HasValue)
        {
            q = q.Where(
                x =>
                    x.CreatedAt <=
                    to.Value);
        }

        var rows =
            await q
                .Select(
                    x => new
                    {
                        x.Amount,
                        x.Status
                    })
                .ToListAsync(ct);

        return (
            rows
                .Where(
                    x =>
                        x.Status ==
                        OrderStatus.Succeeded)
                .Sum(x => x.Amount),

            rows.Count(
                x =>
                    x.Status ==
                    OrderStatus.Succeeded),

            rows.Count(
                x =>
                    x.Status ==
                    OrderStatus.Failed),

            rows.Count(
                x =>
                    x.Status ==
                        OrderStatus.Created ||
                    x.Status ==
                        OrderStatus.IntentLaunched)
        );
    }

    // ============================================================
    // SAVE
    // ============================================================

    public Task<int> SaveChangesAsync(
        CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}