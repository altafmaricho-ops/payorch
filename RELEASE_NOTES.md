# PayOrchestrator Upgrade

## Reliability
- Added automatic EF migration at API startup.
- Added idempotent Super Admin bootstrap from `BootstrapSuperAdmin` in appsettings.
- Added `DefaultConnection` compatibility while retaining `Postgres`.
- Added production configuration template.
- Removed dependency on manually running the bootstrap PowerShell script for normal operation.
- Local dashboard now points to the same `dotnet run` HTTPS endpoint (`5001`).

## Dashboard
- Sidebar is vertically scrollable; all navigation remains reachable at normal browser zoom.
- Removed the bottom-left logout control.
- Account + logout controls are now in the top-right.
- Added a top-right Security Activity panel with date filtering for login/logout history.
- Added backend logout audit event.
- Added colorful success/failed/pending collection visualization.
- Added color-coded KPI values and status treatments.

## Important deployment rule
The included migration is intended as the clean baseline for a fresh PayOrchestrator database. For an existing database created by older SQL scripts, baseline/upgrade it deliberately rather than applying the initial migration over existing tables.

## Secret handling
The application supports credentials in appsettings because this was explicitly requested, but real production deployments should move database, JWT, Razorpay and Super Admin secrets to the hosting provider's environment/secret store before publishing.
