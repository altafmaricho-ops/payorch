using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayOrch.Infrastructure.Persistence.Migrations;

[Migration("202610060001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
-- =====================================================================
-- Payment Orchestration Platform — Phase 1 Schema (PostgreSQL 16+)
-- Scope: multi-tenant merchant sites, L1-L4 hierarchy, Razorpay-backed
-- deposit collection, ledger, webhook relay, audit trail, RLS isolation.
-- No raw VPA / PSP secret ever lives outside Infrastructure-layer tables
-- that only service-role connections can read.
-- =====================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "citext";

-- ---------------------------------------------------------------------
-- 1. Hierarchy: users (Super Admin / Admin / Sub-Admin) + Tenants (sites)
-- ---------------------------------------------------------------------

CREATE TYPE user_role AS ENUM ('SuperAdmin', 'Admin', 'SubAdmin');

CREATE TABLE app_users (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    role            user_role NOT NULL,
    parent_id       UUID REFERENCES app_users(id),   -- hierarchy chain (NULL for SuperAdmin)
    email           CITEXT NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    display_name    TEXT NOT NULL,
    credit_balance  NUMERIC(18,2) NOT NULL DEFAULT 0,
    credit_limit    NUMERIC(18,2) NOT NULL DEFAULT 0,
    is_blocked      BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_app_users_parent ON app_users(parent_id);

-- A tenant = one onboarded merchant site (siteA, siteB, ...). Every
-- tenant belongs to exactly one Sub-Admin's book, which belongs to an
-- Admin, which belongs to a Super Admin — this is how the reporting
-- rollup and RLS walls are derived without duplicating the chain.
CREATE TABLE tenants (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    sub_admin_id        UUID NOT NULL REFERENCES app_users(id),
    merchant_code       TEXT NOT NULL UNIQUE,   -- shown to Sub-Admin, e.g. "Merchant 01"
    display_name        TEXT NOT NULL,          -- real name, hidden from Sub-Admin/L4 views
    vertical            TEXT NOT NULL,          -- e-commerce | ott | edtech | rewards | marketplace | other
    callback_url        TEXT NOT NULL,          -- where we relay verified payment events
    commission_percent  NUMERIC(5,2) NOT NULL DEFAULT 0,
    api_key             TEXT NOT NULL UNIQUE,   -- public identifier for merchant API calls
    api_secret_hash     TEXT NOT NULL,          -- HMAC signing secret, hashed at rest
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_tenants_sub_admin ON tenants(sub_admin_id);

-- ---------------------------------------------------------------------
-- 1b. Sellers (BhandaarBox's local merchants) — onboarded by an Admin,
--     each maps to one Razorpay Linked Account. Money from a customer
--     order is split-transferred straight to the seller's linked account
--     by Razorpay Route; BhandaarBox (a Tenant, e.g. "BhandaarBox.com")
--     never pools seller funds itself.
-- ---------------------------------------------------------------------

CREATE TYPE seller_onboarding_status AS ENUM ('PendingReview', 'SubmittedToPsp', 'Active', 'Rejected', 'Suspended');

CREATE TABLE sellers (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id               UUID NOT NULL REFERENCES tenants(id),   -- which storefront, e.g. BhandaarBox
    onboarded_by            UUID NOT NULL REFERENCES app_users(id), -- Admin who added them
    seller_code             TEXT NOT NULL UNIQUE,   -- Sub-Admin-visible generic code
    business_name           TEXT NOT NULL,          -- real name, hidden from Sub-Admin views
    contact_phone           TEXT NOT NULL,
    contact_email           CITEXT,
    upi_collection_app      TEXT,                   -- informational only: 'gpay_business' | 'phonepe_business' | 'paytm_business' | 'other' — NOT used for money movement, see note below
    psp_code                TEXT NOT NULL DEFAULT 'razorpay',
    psp_linked_account_id   TEXT,                   -- Razorpay Route "account_id" (acc_XXXX) once created
    commission_percent      NUMERIC(5,2) NOT NULL DEFAULT 0,
    status                  seller_onboarding_status NOT NULL DEFAULT 'PendingReview',
    status_reason           TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_sellers_tenant ON sellers(tenant_id);
CREATE INDEX idx_sellers_status ON sellers(status);

-- NOTE on upi_collection_app: this column is deliberately informational
-- (e.g. so an Admin remembers "this seller currently uses GPay Business
-- day-to-day") and plays NO role in payment routing. Money never touches
-- a seller's personal/business VPA directly — it moves customer -> Razorpay
-- -> seller's Razorpay-verified bank account (their Linked Account), which
-- is what keeps this compliant without BhandaarBox holding a PA license.

CREATE TABLE wallets (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL UNIQUE REFERENCES tenants(id),
    balance         NUMERIC(18,2) NOT NULL DEFAULT 0,  -- derived; never written directly, see ledger
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- IMPORTANT: any code path that reads a wallet row in order to write a new
-- balance MUST do so with `SELECT ... FOR UPDATE` inside the same
-- transaction as the balance write + ledger insert. Without that row lock,
-- two concurrent webhooks for the same wallet (e.g. two fast back-to-back
-- deposits) can both read the same starting balance and one credit gets
-- silently lost. See EfUnitOfWork.ApplyLedgerEntryAsync.

-- ---------------------------------------------------------------------
-- 2. PSP configuration — no raw VPA strings; PSP handles collection.
--    Only the Infrastructure layer (service-role DB connection) reads
--    psp_credentials. It never appears in any tenant/sub-admin query path.
-- ---------------------------------------------------------------------

CREATE TABLE psp_credentials (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    psp_code        TEXT NOT NULL,              -- 'razorpay' | 'cashfree' | 'phonepe_pg' | 'paytm_pg'
    is_primary      BOOLEAN NOT NULL DEFAULT FALSE,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    key_id          TEXT NOT NULL,
    key_secret_enc  BYTEA NOT NULL,             -- pgcrypto-encrypted
    webhook_secret_enc BYTEA NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------
-- 3. Orders (deposits) — one row per collection attempt
-- ---------------------------------------------------------------------

CREATE TYPE order_status AS ENUM ('Created', 'IntentLaunched', 'Succeeded', 'Failed', 'Expired');

CREATE TABLE orders (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id),
    seller_id           UUID REFERENCES sellers(id),   -- which local merchant this order's goods belong to (marketplace split model)
    merchant_order_ref  TEXT NOT NULL,          -- tenant's own reference, for their reconciliation
    amount              NUMERIC(18,2) NOT NULL CHECK (amount > 0),
    currency            CHAR(3) NOT NULL DEFAULT 'INR',
    status              order_status NOT NULL DEFAULT 'Created',
    psp_code            TEXT NOT NULL,
    psp_order_id        TEXT,                   -- PSP's order/txn identifier
    psp_payment_id      TEXT,                   -- PSP's actual payment id (needed for split transfers)
    payer_vpa_masked    TEXT,                   -- e.g. cust****@oksbi — masked before it ever hits this row
    customer_ref        TEXT,                   -- tenant-supplied opaque customer id
    device_fingerprint  TEXT,
    ip_address          INET,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at          TIMESTAMPTZ NOT NULL,
    completed_at        TIMESTAMPTZ,
    UNIQUE (tenant_id, merchant_order_ref)
);

CREATE INDEX idx_orders_tenant ON orders(tenant_id);
CREATE INDEX idx_orders_seller ON orders(seller_id);
CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_orders_psp_order_id ON orders(psp_order_id);

-- ---------------------------------------------------------------------
-- 3b. Split transfers — Razorpay Route transfer created against an order,
--     moving the seller's share directly to their Linked Account. This
--     table is BhandaarBox's own record of what Razorpay was told to do;
--     Razorpay is the system of record for whether money actually moved.
-- ---------------------------------------------------------------------

CREATE TYPE transfer_status AS ENUM ('Pending', 'Processed', 'Failed', 'Reversed');

CREATE TABLE seller_transfers (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id            UUID NOT NULL REFERENCES orders(id),
    seller_id           UUID NOT NULL REFERENCES sellers(id),
    psp_transfer_id     TEXT,               -- Razorpay transfer id (trf_XXXX)
    seller_amount       NUMERIC(18,2) NOT NULL,
    platform_commission NUMERIC(18,2) NOT NULL,
    status              transfer_status NOT NULL DEFAULT 'Pending',
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    settled_at          TIMESTAMPTZ
);

CREATE INDEX idx_seller_transfers_order ON seller_transfers(order_id);
CREATE INDEX idx_seller_transfers_seller ON seller_transfers(seller_id);

-- ---------------------------------------------------------------------
-- 4. Double-entry ledger — balances are ALWAYS derived, never mutated
-- ---------------------------------------------------------------------

CREATE TYPE ledger_direction AS ENUM ('Credit', 'Debit');

CREATE TABLE ledger_entries (
    id              BIGSERIAL PRIMARY KEY,
    wallet_id       UUID NOT NULL REFERENCES wallets(id),
    direction       ledger_direction NOT NULL,
    amount          NUMERIC(18,2) NOT NULL CHECK (amount > 0),
    ref_type        TEXT NOT NULL,      -- 'deposit' | 'commission' | 'payout' | 'payout_reversal'
    ref_id          UUID NOT NULL,      -- e.g. orders.id
    balance_after   NUMERIC(18,2) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_ledger_wallet ON ledger_entries(wallet_id);
CREATE INDEX idx_ledger_ref ON ledger_entries(ref_type, ref_id);

-- ---------------------------------------------------------------------
-- 5. Webhook inbox — idempotent PSP callback ingestion, split into a fast
--    INGEST phase (API request path: verify + parse + insert, return 200)
--    and a background PROCESS phase (order/ledger/transfer/relay), so a
--    downstream slowdown never risks Razorpay's webhook retry/timeout.
-- ---------------------------------------------------------------------

CREATE TYPE psp_event_outcome AS ENUM ('Unknown', 'Success', 'Failed');

CREATE TABLE webhook_events (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    psp_code            TEXT NOT NULL,
    psp_event_id        TEXT NOT NULL,      -- PSP's own idempotency key
    payload             JSONB NOT NULL,
    signature_valid     BOOLEAN NOT NULL,
    psp_order_id        TEXT,
    psp_payment_id      TEXT,
    outcome             psp_event_outcome NOT NULL DEFAULT 'Unknown',
    payer_vpa_masked    TEXT,               -- masked at ingest time; raw VPA never lands here
    processing_started_at TIMESTAMPTZ,      -- worker lease: claimed-but-not-yet-committed marker
    processed_at        TIMESTAMPTZ,
    order_id            UUID REFERENCES orders(id),
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (psp_code, psp_event_id)
);

CREATE INDEX idx_webhook_events_pending ON webhook_events(received_at)
    WHERE processed_at IS NULL AND signature_valid = true;

-- ---------------------------------------------------------------------
-- 6. Callback relay — outbound notifications back to merchant siteB etc.
-- ---------------------------------------------------------------------

CREATE TYPE relay_status AS ENUM ('Pending', 'Delivered', 'Failed', 'Exhausted');

CREATE TABLE callback_relays (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id        UUID NOT NULL REFERENCES orders(id),
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    target_url      TEXT NOT NULL,
    payload         JSONB NOT NULL,
    status          relay_status NOT NULL DEFAULT 'Pending',
    attempt_count   INT NOT NULL DEFAULT 0,
    last_attempt_at TIMESTAMPTZ,
    next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_response_code INT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_relay_pending ON callback_relays(status, next_attempt_at);

-- ---------------------------------------------------------------------
-- 7. Fraud primitives (Phase 2 will expand; tables land now so the
--    order-creation path can already write into them)
-- ---------------------------------------------------------------------

CREATE TABLE blocked_identities (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    identity_type   TEXT NOT NULL,   -- 'device' | 'ip' | 'phone' | 'upi' | 'name'
    identity_hash   TEXT NOT NULL,
    reason          TEXT,
    flagged_by      UUID REFERENCES app_users(id),
    flagged_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at      TIMESTAMPTZ,     -- NULL = permanent
    UNIQUE (identity_type, identity_hash)
);

-- ---------------------------------------------------------------------
-- 8. Audit log — append-only, every privileged action
-- ---------------------------------------------------------------------

CREATE TABLE audit_log (
    id              BIGSERIAL PRIMARY KEY,
    actor_id        UUID REFERENCES app_users(id),
    action          TEXT NOT NULL,       -- 'export_attempt' | 'red_flag' | 'credit_adjust' | ...
    target_type     TEXT,
    target_id       TEXT,
    metadata        JSONB,
    allowed         BOOLEAN NOT NULL,    -- record denials too, esp. export attempts
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------
-- 9. Row-Level Security — enforced at the DB layer regardless of app bugs
-- ---------------------------------------------------------------------

ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE wallets ENABLE ROW LEVEL SECURITY;
ALTER TABLE ledger_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE sellers ENABLE ROW LEVEL SECURITY;
ALTER TABLE seller_transfers ENABLE ROW LEVEL SECURITY;

-- Session variables set per connection from the JWT after auth:
--   app.current_role        -> 'SuperAdmin' | 'Admin' | 'SubAdmin'
--   app.current_user_id     -> app_users.id
-- SuperAdmin/Admin bypass via role check; SubAdmin restricted to the
-- tenants under their own sub_admin_id.

CREATE POLICY tenant_subadmin_isolation ON tenants
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR sub_admin_id = current_setting('app.current_user_id', true)::uuid
    );

CREATE POLICY order_subadmin_isolation ON orders
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR tenant_id IN (
            SELECT id FROM tenants
            WHERE sub_admin_id = current_setting('app.current_user_id', true)::uuid
        )
    );

CREATE POLICY wallet_subadmin_isolation ON wallets
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR tenant_id IN (
            SELECT id FROM tenants
            WHERE sub_admin_id = current_setting('app.current_user_id', true)::uuid
        )
    );

CREATE POLICY ledger_subadmin_isolation ON ledger_entries
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR wallet_id IN (
            SELECT w.id FROM wallets w
            JOIN tenants t ON t.id = w.tenant_id
            WHERE t.sub_admin_id = current_setting('app.current_user_id', true)::uuid
        )
    );

-- Note: merchant-identity masking (display_name -> merchant_code) for
-- Sub-Admin views is enforced in the Application layer's read models
-- (never expose tenants.display_name to a SubAdmin-scoped query), since
-- RLS controls row visibility, not column visibility. A SECURITY DEFINER
-- view (v_tenants_subadmin) is added in migration 002 for that purpose.

CREATE POLICY seller_subadmin_isolation ON sellers
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR tenant_id IN (
            SELECT id FROM tenants
            WHERE sub_admin_id = current_setting('app.current_user_id', true)::uuid
        )
    );

CREATE POLICY seller_transfer_subadmin_isolation ON seller_transfers
    USING (
        current_setting('app.current_role', true) IN ('SuperAdmin', 'Admin')
        OR seller_id IN (
            SELECT s.id FROM sellers s
            JOIN tenants t ON t.id = s.tenant_id
            WHERE t.sub_admin_id = current_setting('app.current_user_id', true)::uuid
        )
    );

-- ---------------------------------------------------------------------
-- 11. Column-level masking for Sub-Admins: a SECURITY DEFINER view that
--     substitutes seller_code for business_name and hides contact details.
--     RLS above still restricts ROWS; this view restricts COLUMNS. The
--     application layer queries this view (never the raw `sellers` table)
--     whenever the caller's role is SubAdmin.
-- ---------------------------------------------------------------------

CREATE VIEW v_sellers_subadmin AS
SELECT
    id,
    tenant_id,
    seller_code,
    'Merchant ' || seller_code AS masked_label,
    status,
    commission_percent,
    created_at
FROM sellers;

COMMENT ON VIEW v_sellers_subadmin IS
    'Sub-Admin-safe projection of sellers: no business_name, no contact_phone/email, no psp_linked_account_id.';

-- =====================================================================
-- PayOrchestrator Phase 2 extensions
-- =====================================================================

CREATE TABLE IF NOT EXISTS payment_providers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    owner_user_id UUID NOT NULL REFERENCES app_users(id),
    tenant_id UUID REFERENCES tenants(id),
    provider_code TEXT NOT NULL,
    display_name TEXT NOT NULL,
    account_label TEXT NOT NULL,
    merchant_account_ref TEXT,
    key_id TEXT,
    key_secret_enc BYTEA,
    webhook_secret_enc BYTEA,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_test_mode BOOLEAN NOT NULL DEFAULT TRUE,
    health_status TEXT NOT NULL DEFAULT 'Unknown',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_payment_providers_owner ON payment_providers(owner_user_id);
CREATE INDEX IF NOT EXISTS idx_payment_providers_tenant ON payment_providers(tenant_id);

CREATE TABLE IF NOT EXISTS routing_rules (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    owner_user_id UUID NOT NULL REFERENCES app_users(id),
    tenant_id UUID REFERENCES tenants(id),
    payment_provider_id UUID NOT NULL REFERENCES payment_providers(id),
    min_amount NUMERIC(18,2) NOT NULL CHECK (min_amount >= 0),
    max_amount NUMERIC(18,2),
    priority INT NOT NULL DEFAULT 100,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    fallback_provider_id UUID REFERENCES payment_providers(id),
    payment_method TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (max_amount IS NULL OR max_amount >= min_amount)
);
CREATE INDEX IF NOT EXISTS idx_routing_rules_scope ON routing_rules(owner_user_id, tenant_id, is_enabled, priority);

CREATE TABLE IF NOT EXISTS user_permissions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    permission_code TEXT NOT NULL,
    tenant_id UUID REFERENCES tenants(id) ON DELETE CASCADE,
    granted BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE(user_id, permission_code, tenant_id)
);
CREATE INDEX IF NOT EXISTS idx_user_permissions_user ON user_permissions(user_id);

CREATE TABLE IF NOT EXISTS user_tenant_access (
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    PRIMARY KEY(user_id, tenant_id)
);

CREATE TABLE IF NOT EXISTS refunds (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id UUID NOT NULL REFERENCES orders(id),
    requested_by UUID REFERENCES app_users(id),
    amount NUMERIC(18,2) NOT NULL CHECK (amount > 0),
    reason TEXT,
    psp_refund_id TEXT,
    status TEXT NOT NULL DEFAULT 'Pending',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_refunds_order ON refunds(order_id);

ALTER TABLE orders ADD COLUMN IF NOT EXISTS customer_name TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS customer_email CITEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS customer_phone TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS metadata JSONB;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS routing_rule_id UUID REFERENCES routing_rules(id);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS payment_provider_id UUID REFERENCES payment_providers(id);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS failure_reason TEXT;

-- Safe compatibility columns for dashboard drill-downs.
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMPTZ;



-- PayOrchestrator Phase 3 control-plane hardening.
ALTER TYPE relay_status ADD VALUE IF NOT EXISTS 'Processing';
-- Run this once against the existing payorch database before starting the
-- updated API. It is intentionally idempotent so it can be re-run safely.

ALTER TABLE app_users ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'Active';
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS status_reason TEXT;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS approved_by UUID REFERENCES app_users(id);
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS token_version INT NOT NULL DEFAULT 0;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMPTZ;

ALTER TABLE tenants ADD COLUMN IF NOT EXISTS callback_secret TEXT NOT NULL DEFAULT encode(gen_random_bytes(32), 'hex');
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'Active';
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS status_reason TEXT;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS approved_by UUID REFERENCES app_users(id);
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

-- Existing rows are trusted legacy active records. New application-created
-- users/websites explicitly enter PendingApproval where required.
UPDATE app_users SET status = 'Blocked' WHERE is_blocked = true AND status = 'Active';
UPDATE app_users SET status = 'Active' WHERE status IS NULL OR status = '';
UPDATE tenants SET status = 'Suspended' WHERE is_active = false AND status = 'Active';
UPDATE tenants SET updated_at = COALESCE(updated_at, created_at);

CREATE INDEX IF NOT EXISTS idx_app_users_status ON app_users(status);
CREATE INDEX IF NOT EXISTS idx_tenants_status ON tenants(status);
CREATE INDEX IF NOT EXISTS idx_audit_log_created_at ON audit_log(created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_ref_once ON ledger_entries(ref_type, ref_id);

-- Keep the callback secret independent from the one-way API-secret hash.
COMMENT ON COLUMN tenants.callback_secret IS 'Reversible deployment secret used only for outbound merchant callback HMAC signatures.';
COMMENT ON COLUMN app_users.token_version IS 'Increment to revoke all JWTs issued before a lifecycle/security change.';

""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;");
    }
}
