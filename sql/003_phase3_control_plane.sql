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
