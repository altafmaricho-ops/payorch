# PayOrchestrator — Production Setup

This version is designed to remove the two recurring local problems:

1. Recreated PostgreSQL database -> missing tables.
2. Recreated PostgreSQL database -> missing Super Admin.

## What is now automatic

On API startup:

1. EF Core applies the included `InitialCreate` migration.
2. The full PostgreSQL control-plane schema is created.
3. The configured Super Admin is created if the configured email does not exist.
4. Existing Super Admin is left intact; the password is not overwritten on every restart.
5. Login/logout events are audited.

You no longer need to run `dotnet ef database update` after a clean database creation.

## 1. Configure production settings

Edit `src/PayOrch.Api/appsettings.Production.json` and replace every `REPLACE_ME` / `YOUR_*` value.

Keep these secrets private:

- PostgreSQL password
- JWT signing key
- Razorpay KeySecret
- Razorpay WebhookSecret
- Super Admin password

For a real public deployment, prefer hosting-provider environment variables/secret storage over committing secrets to source control.

## 2. Use a clean production database for the first deployment

The included migration is the baseline for a fresh PayOrchestrator database.

If a production database already contains the old SQL schema but does not contain `__EFMigrationsHistory`, do not blindly start the new version. Either:

- deploy to a new empty database (recommended for the first production release), or
- baseline/import the existing schema deliberately before enabling automatic migration.

## 3. Start API

```powershell
dotnet run --project .\src\PayOrch.Api
```

The API applies the migration before background workers start.

Check:

- `/health/live` — process health
- `/health` — process + database
- `/health/ready` — database readiness

## 4. Super Admin

Credentials are read from:

```json
"BootstrapSuperAdmin": {
  "Enabled": true,
  "Email": "...",
  "DisplayName": "...",
  "Password": "..."
}
```

If the database is recreated, the next API startup recreates the Super Admin automatically.

The password is hashed with ASP.NET Core `PasswordHasher`; plaintext is never stored in PostgreSQL.

## 5. Dashboard

```powershell
cd .\dashboard
npm install
npm run build
```

Set `VITE_API_BASE_URL` in `dashboard/.env` to the public API URL before building.

## 6. Merchant integration

Use the merchant API key in the `X-Api-Key` header. Never expose PSP KeySecret/WebhookSecret in browser code.

## 7. Razorpay Live mode

Only enable live keys after:

- `/health` is green
- public HTTPS webhook URL is reachable
- Razorpay webhook secret is configured identically in Razorpay and PayOrchestrator
- routing/provider health is green
- a small-value transaction is planned

Do not use a local `localhost` webhook URL for a real Razorpay transaction.
