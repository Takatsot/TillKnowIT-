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
`ShopCoreDbContext` (shopcore), `MarketplaceDbContext` (marketplace), `MessagingDbContext` (messaging).

`bash backend/scripts/check-migrations.sh` fails if a model change has no migration. CI runs it on every pull request.

Staging and production: build a migration bundle per context
(`dotnet ef migrations bundle --context ...`) and run it as a deploy step. Never edit the database by hand.
Set `TILLKNOWIT_DB` to point `dotnet ef` at a different server.
