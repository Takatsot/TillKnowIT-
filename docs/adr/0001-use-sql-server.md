# ADR 0001: Use SQL Server (Azure SQL) instead of PostgreSQL

Status: Accepted, 2026-10-26. This replaces the "Azure Database for PostgreSQL 18" choice in the Phase 1 spec.

## Context

The Phase 1 spec chose PostgreSQL. SQL Server is TFG's standard and is what the team knows and can support.
The rest of the architecture (one schema per module, EF Core code first, transactional outbox) does not depend on the database engine.

## Decision

Use SQL Server 2022 or later for local development, and Azure SQL Database in Azure South Africa North.
One database, one schema per module, as before.

## Consequences

- **Offline sync:** PowerSync supports SQL Server and Azure SQL as a source database, but at Beta status and through Change Data Capture (CDC) polling, not logical replication.
  CDC must be enabled on the database and on every synced table. This needs a spike in Sprint 0: run the PowerSync test (one product saved on a phone, synced, shown on a PC; a budget phone offline for a day) against SQL Server before any sync code is built on it.
  If the spike fails, the fallback is a small sync service of our own on top of the append-only tables.
- **Version:** SQL Server 2022 or later, because PowerSync's SQL Server source needs it.
- **JSON columns:** per-language names, outbox payloads and invoice results are stored as `nvarchar(max)` JSON text, not `jsonb`.
- **Outbox claiming:** `UPDLOCK, READPAST, ROWLOCK` replaces `FOR UPDATE SKIP LOCKED`.
- **Report functions:** the files in `database/functions/` are written in T-SQL.
- **Primary keys:** ids stay UUID v7. SQL Server sorts GUIDs in a different byte order, so the time-ordering benefit for the clustered index is lost.
  At pilot volume (about 300 sales a day per shop, 20 to 30 shops) the extra page splits are negligible. Revisit if volume grows.
- **Row-level security:** done with SQL Server security policies and predicate functions.
- **Cascade paths:** SQL Server rejects multiple cascade paths to one table. The migrations have been generated but not yet applied to a real server, so this is checked the first time they run.
