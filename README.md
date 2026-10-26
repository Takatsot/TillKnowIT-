# TillKnowIT

Phase 1 backend: .NET 10 modular monolith, EF Core code-first, one PostgreSQL schema per module.

## Run locally

```
docker compose up -d                       # PostgreSQL 18
cd backend
dotnet run --project src/Host/TillKnowIT.Api   # Development applies every module's migrations on start
dotnet test
```

## Migrations (one history table per module schema)

Add a migration to a module (example: ShopCore):

```
cd backend
dotnet ef migrations add <Name> \
  --context ShopCoreDbContext \
  --project src/Modules/ShopCore/ShopCore.Infrastructure \
  --startup-project src/Host/TillKnowIT.Api \
  --output-dir Persistence/Migrations
```

Contexts: `IdentityDbContext` (identity), `CatalogueDbContext` (catalogue), `ShopCoreDbContext` (shopcore).

Staging and production: build a migration bundle per context
(`dotnet ef migrations bundle --context ...`) and run it as a deploy step. Never edit the database by hand.
Set `TILLKNOWIT_DB` to point `dotnet ef` at a different server.
