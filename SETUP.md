# PayOrchestrator — local setup

## What is included

This build keeps the existing PayOrchestrator control plane and adds a stable merchant-facing v1 payment API plus a more operationally focused admin dashboard.

### Merchant integration
- `POST /api/v1/payments`
- `GET /api/v1/payments/{paymentId}`
- `GET /api/v1/payments/by-reference/{merchantOrderId}`
- Signed merchant callback webhook using `X-PayOrch-Signature`
- Smart routing remains internal to PayOrchestrator

### Dashboard
- Secure JWT login/logout
- Role-based navigation for SuperAdmin/Admin/SubAdmin
- Merchant website selector
- Date/time payment filters
- Payment status and text search
- Payment detail and refund operation
- CSV export with the selected merchant/date/status filters
- Reports & audit workspace
- Risk controls, PSP accounts and smart routing
- Live payment lifecycle feed

## Local configuration

1. PostgreSQL must contain the PayOrchestrator schema. Run the SQL files in `sql/` in order against the `payorch` database. Apply `003_phase3_control_plane.sql` to an existing Phase 3 database.
2. Copy the PostgreSQL connection string into `src/PayOrch.Api/appsettings.json` or a user-secret/environment variable.
3. Set a strong JWT signing key.
4. Configure Razorpay credentials in the appropriate environment/secret store.
5. Run the API on the HTTPS port from `src/PayOrch.Api/Properties/launchSettings.json` (the dashboard is configured for `https://localhost:44333`).
6. In `dashboard/`, run `npm install` and then `npm run dev`.
7. Open `http://localhost:5173`.

Never commit live PSP secrets, database passwords or JWT signing keys.

## Merchant quick start

See `docs/MERCHANT-INTEGRATION.md` for the integration contract and webhook verification details.
