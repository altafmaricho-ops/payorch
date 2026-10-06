# PayOrchestrator Dashboard

React + TypeScript + Vite control-plane dashboard for SuperAdmin, Admin and SubAdmin users.

## Environment

```env
VITE_API_BASE_URL=https://localhost:44333
VITE_HUB_URL=https://localhost:44333/hubs/orders
```

## Run

```powershell
npm install
npm run dev
```

The frontend stores the short-lived JWT in browser storage for the local control-panel session. The API validates the JWT and also checks the user's current lifecycle status/token version on every authenticated request.
