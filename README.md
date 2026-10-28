# TillKnowIT

.NET 10 modular monolith, EF Core code-first, one SQL Server schema per module, and a React PWA till. Phases 1 to 5 of the specs in `docs/specs/` are built as
backend code with tests; what is real, what runs only against pretend partners, and what is still to do is kept honest in [docs/STATUS.md](docs/STATUS.md).
The database choice is recorded in [docs/adr/0001-use-sql-server.md](docs/adr/0001-use-sql-server.md).

Modules: ShopCore, Catalogue, Marketplace, Messaging, Identity, Payments, Wallet, Platform (business packs and the partner API), Customers, Bookings, Billing,
Finance, Insights, Accounting. A module reaches another only through its Contracts project; architecture tests enforce it.

## Run locally

```
docker compose up -d                       # SQL Server 2022 on localhost,1433
cd backend
dotnet run --project src/Host/TillKnowIT.Api      # Development applies every module's migrations on start
dotnet run --project src/Host/TillKnowIT.Worker   # publishes the outbox of every module
dotnet test
```

The local `sa` password is in `docker-compose.yml` and `appsettings.Development.json`. It is for local use only.

## Migrations (one history table per module schema)

Add a migration to a module (example: ShopCore):

```
cd backend
dotnet tool restore
dotnet ef migrations add <Name> \
  --context ShopCoreDbContext \
  --project src/Modules/ShopCore/ShopCore.Infrastructure \
  --startup-project src/Host/TillKnowIT.Api \
  --output-dir Persistence/Migrations
```

Contexts: one `<Module>DbContext` per module, each with its own schema (identity, catalogue, shopcore, marketplace, messaging, payments, wallet, platform,
customers, bookings, billing, finance, insights, accounting). New module: `python backend/scripts/new_module.py <Name> <schema>`. While a module's Initial
migration is unapplied, `bash backend/scripts/new_migration.sh <Module> --reset` regenerates it.

`bash backend/scripts/check-migrations.sh` fails if a model change has no migration. CI runs it on every pull request.

Staging and production: build a migration bundle per context
(`dotnet ef migrations bundle --context ...`) and run it as a deploy step. Never edit the database by hand.
Set `TILLKNOWIT_DB` to point `dotnet ef` at a different server.

## Frontend

React PWA for the till, in `frontend/` (npm workspaces). Needs Node 24.

```
cd frontend
npm install
npm run dev        # till app on http://localhost:5173, with two example staff (dev builds only)
npm test           # domain rules, i18n, hardware, packs, the PIN unlock and selling screens, the offline queue
npm run build      # PWA build with service worker
```

- `packages/domain`: money, sale, stock and PIN rules in TypeScript. Error codes match the C# server rules, and the
  PIN hash format is checked on both sides with one shared test vector.
- `packages/i18n`: eleven languages. English is complete; the others show English until translated and reviewed by first-language speakers.
- `packages/hardware`: printer, cash drawer, scale and kitchen-screen ports, the ESC/POS encoder, slip layouts and fakes. No real device adapter yet.
- `packages/packs`: reads the business pack the server sends (which features a shop has).
  Run tests with `REQUIRE_FULL_TRANSLATION=1` to fail on missing translations.
- `packages/design-system`: tokens and components. Touch targets are at least 48px.
- `apps/till`: the till. Dev builds have example staff (Thandi 1234, Sipho 4321) and a few example products; production builds have none of either.
  Sales go into an IndexedDB queue first and are sent when there is a connection (set `tillknowit.device` to `{"shopId": "...", "deviceId": "..."}` once registered).
