using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using PayOrch.Domain.Entities;

namespace PayOrch.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<CallbackRelay> CallbackRelays => Set<CallbackRelay>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<SellerTransfer> SellerTransfers => Set<SellerTransfer>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<BlockedIdentity> BlockedIdentities => Set<BlockedIdentity>();
    public DbSet<PaymentProvider> PaymentProviders => Set<PaymentProvider>();
    public DbSet<RoutingRule> RoutingRules => Set<RoutingRule>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserTenantAccess> UserTenantAccess => Set<UserTenantAccess>();
    public DbSet<Refund> Refunds => Set<Refund>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ============================================================
        // POSTGRES ENUM  (FIX: makes the migration emit CREATE TYPE user_role)
        // The name translator must match the one used in Program.cs
        // (MapEnum<UserRole>("user_role", new NpgsqlNullNameTranslator())).
        // ============================================================
        b.HasPostgresEnum<UserRole>(
            name: "user_role",
            nameTranslator: new NpgsqlNullNameTranslator());

        // ============================================================
        // APP USERS
        // ============================================================
        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_users");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Role).HasColumnName("role").HasColumnType("user_role");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.CreditBalance).HasColumnName("credit_balance");
            e.Property(x => x.CreditLimit).HasColumnName("credit_limit");
            e.Property(x => x.IsBlocked).HasColumnName("is_blocked");
            e.Property(x => x.StatusReason).HasColumnName("status_reason");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.TokenVersion).HasColumnName("token_version");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => new { x.ParentId, x.Role, x.Status });
        });

        // ============================================================
        // TENANTS / WEBSITES
        // ============================================================
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SubAdminId).HasColumnName("sub_admin_id");
            e.Property(x => x.MerchantCode).HasColumnName("merchant_code");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.Vertical).HasColumnName("vertical");
            e.Property(x => x.CallbackUrl).HasColumnName("callback_url");
            e.Property(x => x.CommissionPercent).HasColumnName("commission_percent");
            e.Property(x => x.ApiKey).HasColumnName("api_key");
            e.Property(x => x.ApiSecretHash).HasColumnName("api_secret_hash");
            e.Property(x => x.CallbackSecret).HasColumnName("callback_secret");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.StatusReason).HasColumnName("status_reason");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => x.MerchantCode).IsUnique();
            e.HasIndex(x => x.ApiKey).IsUnique();
            e.HasIndex(x => new { x.SubAdminId, x.Status });
        });

        // ============================================================
        // PAYMENT PROVIDERS
        // ============================================================
        b.Entity<PaymentProvider>(e =>
        {
            e.ToTable("payment_providers");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.ProviderCode).HasColumnName("provider_code");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.AccountLabel).HasColumnName("account_label");
            e.Property(x => x.MerchantAccountRef).HasColumnName("merchant_account_ref");
            e.Property(x => x.KeyId).HasColumnName("key_id");
            e.Property(x => x.KeySecretEncrypted).HasColumnName("key_secret_enc");
            e.Property(x => x.WebhookSecretEncrypted).HasColumnName("webhook_secret_enc");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.IsTestMode).HasColumnName("is_test_mode");
            e.Property(x => x.HealthStatus).HasColumnName("health_status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ============================================================
        // ROUTING RULES
        // ============================================================
        b.Entity<RoutingRule>(e =>
        {
            e.ToTable("routing_rules");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.PaymentProviderId).HasColumnName("payment_provider_id");
            e.Property(x => x.MinAmount).HasColumnName("min_amount");
            e.Property(x => x.MaxAmount).HasColumnName("max_amount");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.FallbackProviderId).HasColumnName("fallback_provider_id");
            e.Property(x => x.PaymentMethod).HasColumnName("payment_method");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ============================================================
        // USER PERMISSIONS
        // ============================================================
        b.Entity<UserPermission>(e =>
        {
            e.ToTable("user_permissions");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PermissionCode).HasColumnName("permission_code");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Granted).HasColumnName("granted");

            e.HasIndex(x => new { x.UserId, x.PermissionCode, x.TenantId }).IsUnique();
        });

        // ============================================================
        // USER / TENANT ACCESS
        // ============================================================
        b.Entity<UserTenantAccess>(e =>
        {
            e.ToTable("user_tenant_access");
            e.HasKey(x => new { x.UserId, x.TenantId });

            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
        });

        // ============================================================
        // REFUNDS
        // ============================================================
        b.Entity<Refund>(e =>
        {
            e.ToTable("refunds");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.RequestedBy).HasColumnName("requested_by");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.PspRefundId).HasColumnName("psp_refund_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });

        // ============================================================
        // WALLETS
        // ============================================================
        b.Entity<Wallet>(e =>
        {
            e.ToTable("wallets");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Balance).HasColumnName("balance");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ============================================================
        // LEDGER
        // ============================================================
        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entries");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WalletId).HasColumnName("wallet_id");
            e.Property(x => x.Direction).HasConversion<string>().HasColumnName("direction");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.RefType).HasColumnName("ref_type");
            e.Property(x => x.RefId).HasColumnName("ref_id");
            e.Property(x => x.BalanceAfter).HasColumnName("balance_after");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ============================================================
        // ORDERS
        // ============================================================
        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.SellerId).HasColumnName("seller_id");
            e.Property(x => x.MerchantOrderRef).HasColumnName("merchant_order_ref");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.Currency).HasColumnName("currency");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.PspCode).HasColumnName("psp_code");
            e.Property(x => x.PspOrderId).HasColumnName("psp_order_id");
            e.Property(x => x.PspPaymentId).HasColumnName("psp_payment_id");
            e.Property(x => x.PayerVpaMasked).HasColumnName("payer_vpa_masked");
            e.Property(x => x.CustomerRef).HasColumnName("customer_ref");
            e.Property(x => x.DeviceFingerprint).HasColumnName("device_fingerprint");
            e.Property(x => x.IpAddress).HasColumnName("ip_address");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CustomerName).HasColumnName("customer_name");
            e.Property(x => x.CustomerEmail).HasColumnName("customer_email");
            e.Property(x => x.CustomerPhone).HasColumnName("customer_phone");
            e.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.RoutingRuleId).HasColumnName("routing_rule_id");
            e.Property(x => x.PaymentProviderId).HasColumnName("payment_provider_id");
            e.Property(x => x.FailureReason).HasColumnName("failure_reason");

            e.HasIndex(x => new { x.TenantId, x.MerchantOrderRef }).IsUnique();
        });

        // ============================================================
        // WEBHOOK EVENTS
        // ============================================================
        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PspCode).HasColumnName("psp_code");
            e.Property(x => x.PspEventId).HasColumnName("psp_event_id");
            e.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.SignatureValid).HasColumnName("signature_valid");
            e.Property(x => x.PspOrderId).HasColumnName("psp_order_id");
            e.Property(x => x.PspPaymentId).HasColumnName("psp_payment_id");
            e.Property(x => x.Outcome).HasConversion<string>().HasColumnName("outcome");
            e.Property(x => x.PayerVpaMasked).HasColumnName("payer_vpa_masked");
            e.Property(x => x.ProcessingStartedAt).HasColumnName("processing_started_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");

            e.HasIndex(x => new { x.PspCode, x.PspEventId }).IsUnique();
        });

        // ============================================================
        // CALLBACK RELAYS
        // ============================================================
        b.Entity<CallbackRelay>(e =>
        {
            e.ToTable("callback_relays");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TargetUrl).HasColumnName("target_url");
            e.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at");
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            e.Property(x => x.LastResponseCode).HasColumnName("last_response_code");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ============================================================
        // SELLERS
        // ============================================================
        b.Entity<Seller>(e =>
        {
            e.ToTable("sellers");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.OnboardedBy).HasColumnName("onboarded_by");
            e.Property(x => x.SellerCode).HasColumnName("seller_code");
            e.Property(x => x.BusinessName).HasColumnName("business_name");
            e.Property(x => x.ContactPhone).HasColumnName("contact_phone");
            e.Property(x => x.ContactEmail).HasColumnName("contact_email");
            e.Property(x => x.UpiCollectionApp).HasColumnName("upi_collection_app");
            e.Property(x => x.PspCode).HasColumnName("psp_code");
            e.Property(x => x.PspLinkedAccountId).HasColumnName("psp_linked_account_id");
            e.Property(x => x.CommissionPercent).HasColumnName("commission_percent");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.StatusReason).HasColumnName("status_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ============================================================
        // SELLER TRANSFERS
        // ============================================================
        b.Entity<SellerTransfer>(e =>
        {
            e.ToTable("seller_transfers");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.SellerId).HasColumnName("seller_id");
            e.Property(x => x.PspTransferId).HasColumnName("psp_transfer_id");
            e.Property(x => x.SellerAmount).HasColumnName("seller_amount");
            e.Property(x => x.PlatformCommission).HasColumnName("platform_commission");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.SettledAt).HasColumnName("settled_at");
        });

        // ============================================================
        // AUDIT LOG
        // ============================================================
        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ActorId).HasColumnName("actor_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.TargetType).HasColumnName("target_type");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.Allowed).HasColumnName("allowed");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ============================================================
        // BLOCKED IDENTITIES
        // ============================================================
        b.Entity<BlockedIdentity>(e =>
        {
            e.ToTable("blocked_identities");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IdentityType).HasColumnName("identity_type");
            e.Property(x => x.IdentityHash).HasColumnName("identity_hash");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.FlaggedBy).HasColumnName("flagged_by");
            e.Property(x => x.FlaggedAt).HasColumnName("flagged_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");

            e.HasIndex(x => new { x.IdentityType, x.IdentityHash }).IsUnique();
        });
    }
}