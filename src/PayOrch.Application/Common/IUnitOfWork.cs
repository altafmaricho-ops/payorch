using PayOrch.Domain.Entities;

namespace PayOrch.Application.Common;

public interface IUnitOfWork
{
    Task<Tenant?> GetTenantByApiKeyAsync(string apiKey, CancellationToken ct);
    Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct);
    Task<Wallet> GetOrCreateWalletAsync(Guid tenantId, CancellationToken ct);

    Task AddOrderAsync(Order order, CancellationToken ct);
    Task<Order?> GetOrderByPspOrderIdAsync(string pspOrderId, CancellationToken ct);
    Task<Order?> GetOrderByTenantAndMerchantRefAsync(Guid tenantId, string merchantOrderRef, CancellationToken ct);
    Task<Order?> GetOrderByIdAsync(Guid orderId, CancellationToken ct);
    Task UpdateOrderAsync(Order order, CancellationToken ct);

    Task<bool> WebhookEventExistsAsync(string pspCode, string pspEventId, CancellationToken ct);
    Task AddWebhookEventAsync(WebhookEvent evt, CancellationToken ct);
    Task<IReadOnlyList<WebhookEvent>> ClaimPendingWebhookEventsAsync(int batchSize, TimeSpan leaseTimeout, CancellationToken ct);
    Task MarkWebhookEventProcessedAsync(Guid webhookEventId, Guid? orderId, CancellationToken ct);

    Task<Seller?> GetSellerByIdAsync(Guid sellerId, CancellationToken ct);
    Task<bool> SellerTransferExistsAsync(Guid orderId, Guid sellerId, CancellationToken ct);
    Task AddSellerTransferAsync(SellerTransfer transfer, CancellationToken ct);
    Task<string> GenerateNextSellerCodeAsync(Guid tenantId, CancellationToken ct);
    Task AddSellerAsync(Seller seller, CancellationToken ct);
    Task<IReadOnlyList<Seller>> ListSellersForTenantAsync(Guid tenantId, CancellationToken ct);
    Task UpdateSellerAsync(Seller seller, CancellationToken ct);

    Task<AppUser?> GetUserByIdAsync(Guid userId, CancellationToken ct);
    Task<AppUser?> GetUserByEmailAsync(string email, CancellationToken ct);
    Task<bool> AnySuperAdminExistsAsync(CancellationToken ct);
    Task<IReadOnlyList<AppUser>> ListChildUsersAsync(Guid parentUserId, CancellationToken ct);
    Task AddUserAsync(AppUser user, CancellationToken ct);
    Task UpdateUserAsync(AppUser user, CancellationToken ct);

    Task WriteAuditLogAsync(Guid? actorId, string action, string? targetType, string? targetId, string? metadataJson, bool allowed, CancellationToken ct);
    Task<IReadOnlyList<AuditLogEntry>> ListAuditLogsAsync(CallerContext caller, int take, CancellationToken ct);

    Task<BlockedIdentity?> GetBlockedIdentityAsync(string type, string hash, CancellationToken ct);
    Task AddBlockedIdentityAsync(BlockedIdentity identity, CancellationToken ct);
    Task<bool> IsAnyIdentityBlockedAsync(IEnumerable<(string Type, string Hash)> identities, CancellationToken ct);
    Task<IReadOnlyList<BlockedIdentity>> ListBlockedIdentitiesAsync(int take, CancellationToken ct);

    Task<bool> LedgerEntryExistsAsync(string refType, Guid refId, CancellationToken ct);
    Task ApplyLedgerEntryAsync(Guid walletId, LedgerDirection direction, decimal amount, string refType, Guid refId, CancellationToken ct);
    Task<bool> CallbackRelayExistsAsync(Guid orderId, string outcome, CancellationToken ct);
    Task EnqueueCallbackRelayAsync(CallbackRelay relay, CancellationToken ct);

    Task<PaymentProvider?> GetPaymentProviderAsync(Guid id, CancellationToken ct);
    Task<decimal> GetRefundedAmountAsync(Guid orderId, CancellationToken ct);
    Task AddRefundAsync(Refund refund, CancellationToken ct);
    Task<PaymentProvider?> GetDefaultPaymentProviderAsync(Guid tenantId, CancellationToken ct);
    Task<RoutingRule?> FindRoutingRuleAsync(Guid tenantId, decimal amount, string? paymentMethod, CancellationToken ct);
    Task AddPaymentProviderAsync(PaymentProvider provider, CancellationToken ct);
    Task<IReadOnlyList<PaymentProvider>> ListPaymentProvidersAsync(CallerContext caller, Guid? tenantId, CancellationToken ct);
    Task AddRoutingRuleAsync(RoutingRule rule, CancellationToken ct);
    Task UpdateRoutingRuleAsync(RoutingRule rule, CancellationToken ct);
    Task<IReadOnlyList<RoutingRule>> ListRoutingRulesAsync(CallerContext caller, Guid? tenantId, CancellationToken ct);

    Task<IReadOnlyList<Tenant>> ListTenantsForCallerAsync(CallerContext caller, CancellationToken ct);
    Task<bool> IsTenantInCallerScopeAsync(CallerContext caller, Guid tenantId, CancellationToken ct);
    Task AddTenantAsync(Tenant tenant, CancellationToken ct);
    Task UpdateTenantAsync(Tenant tenant, CancellationToken ct);
    Task<UserPermission?> GetUserPermissionAsync(Guid userId, string permissionCode, Guid? tenantId, CancellationToken ct);
    Task AddUserPermissionAsync(UserPermission permission, CancellationToken ct);
    Task<bool> HasUserTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task AddUserTenantAccessAsync(UserTenantAccess access, CancellationToken ct);
    Task<IReadOnlyList<AppUser>> ListUsersForCallerAsync(CallerContext caller, CancellationToken ct);
    Task<bool> IsUserInCallerScopeAsync(CallerContext caller, Guid userId, CancellationToken ct);

    Task<IReadOnlyList<Order>> ListOrdersForCallerAsync(CallerContext caller, Guid? tenantId, string? status, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct, int take = 500);
    Task<Order?> GetOrderDetailForCallerAsync(CallerContext caller, Guid orderId, CancellationToken ct);
    Task<int> CountActiveTenantsForCallerAsync(CallerContext caller, CancellationToken ct);
    Task<(decimal Total, int Successful, int Failed, int Pending)> GetPaymentMetricsAsync(CallerContext caller, Guid? tenantId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
