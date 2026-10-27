# TillKnowIT

Phase 1 backend: .NET 10 modular monolith, EF Core code-first, one SQL Server schema per module.
The database choice is recorded in [docs/adr/0001-use-sql-server.md](docs/adr/0001-use-sql-server.md).

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

Contexts: `IdentityDbContext` (identity), `CatalogueDbContext` (catalogue, plus the `platform` lookup tables),
`ShopCoreDbContext` (shopcore), `MarketplaceDbContext` (marketplace), `MessagingDbContext` (messaging), `PaymentsDbContext` (payments), `WalletDbContext` (wallet).

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
npm test           # domain rules, i18n and the PIN unlock screen
npm run build      # PWA build with service worker
```

- `packages/domain`: money, sale, stock and PIN rules in TypeScript. Error codes match the C# server rules, and the
  PIN hash format is checked on both sides with one shared test vector.
- `packages/i18n`: six languages. English is complete; other languages show English until translated.
  Run tests with `REQUIRE_FULL_TRANSLATION=1` to fail on missing translations.
- `packages/design-system`: tokens and components. Touch targets are at least 48px.
- `apps/till`: the till. Dev builds have example staff (Thandi 1234, Sipho 4321); production builds have none.
