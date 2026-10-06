# Phase 2 database notes

`001_phase1_schema.sql` is intentionally append-only and now contains the Phase-2 extension block at its end. If your deployment process separates migrations, extract that final block into your migration runner as `002_phase2` rather than executing it twice.

The Phase-2 entities are:

- `payment_providers`
- `routing_rules`
- `user_permissions`
- `user_tenant_access`
- `refunds`
- new payment/customer/routing columns on `orders`

The API currently enforces application-level scope. Before enabling database `FORCE ROW LEVEL SECURITY` in production, wire PostgreSQL session context (`app.current_role` and `app.current_user_id`) at connection/request scope and verify pooled-connection reset semantics.
