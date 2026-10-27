# Product specifications

The five development specs the code is built from. They are copied here unchanged.

| Phase | Spec | Theme |
| --- | --- | --- |
| 1 | [phase-1-development-spec.md](phase-1-development-spec.md) | Offline-first till for the spaza pilot |
| 2 | [phase-2-supplier-marketplace.md](phase-2-supplier-marketplace.md) | Supplier marketplace, ordering, supplier payments, group buying |
| 3 | [phase-3-school-wallet.md](phase-3-school-wallet.md) | School tuckshop wallet |
| 4 | [phase-4-every-business.md](phase-4-every-business.md) | Business packs, hardware, customers and loyalty |
| 5 | [phase-5-growth.md](phase-5-growth.md) | Branches, billing, accounting, pay-later, insights, partner API |

## Where the code differs from the specs

The specs are the plan. Decisions made while building are recorded as ADRs in [../adr](../adr) and listed here so nobody is surprised.

- **SQL Server instead of PostgreSQL.** Phase 1 and later specs say PostgreSQL (including full-text and trigram search in Phase 2 and a
  "PostgreSQL warehouse" in Phase 5). The team standard is SQL Server, so everything is built on SQL Server and Azure SQL.
  See [ADR 0001](../adr/0001-use-sql-server.md). Search uses SQL Server full-text search; the Insights database is a separate SQL Server database.
- **Small data-model additions** found while building (for example `sale_id` on stock movements and `payment_method_code` on credit entries)
  are listed in the commit that added them.

## How far the build has got

[../STATUS.md](../STATUS.md) lists every user story by phase with its state: done, partly done, or not started and why.
Anything that needs an outside partner (payments, finance, accounting packages, NFC and printer hardware, WhatsApp and SMS providers)
is built up to a clearly defined port with a fake for tests, and is marked so in the status list.
