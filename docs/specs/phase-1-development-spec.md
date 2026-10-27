# TillKnowIT – Phase 1 Development Spec

Oct 26, 2026 · @Someone

## Scope of Phase 1

Phase 1 delivers an offline-first till for the spaza pilot: 20–30 shops selling, receiving stock and seeing their numbers on the phones and PCs they already have. The product vision, business model and full stack are in the [TillKnowIT product specification](https://claude.ai/artifact/Gn1vUkz4dGfTLAqcHWJE3j); this document is what developers build from.

**In Phase 1**

- Owner sign-up, shop setup, staff PINs and device registration
- Shared product catalogue and shop products
- Selling offline: tiles, search, barcode scan, cash, card, QR payment and credit book
- Slips on request only: QR code first, WhatsApp, SMS or email
- Stock in: scan, Excel or CSV, and photo or PDF invoice reading
- Stock list, stock counts with reasons, expiry tracking
- Credit book and cash-up
- Insights and reports with PDF, Excel and CSV export
- Compliance document wallet and expiry blocking
- Airtime, data and electricity through one partner
- Owner daily summary and loss alerts
- Six languages
- Field agent setup app
- Small supplier marketplace for a handful of founding suppliers (catalogue and orders only)

**Not in Phase 1**

- School wallet, full marketplace (deals, group buying, in-app supplier payments), printers and cash drawers, business packs other than spaza, loyalty, branches, accounting export, funding provider work.

**Assumptions to confirm from the shop visits**

- Owners will use their own Android phone as the main till.
- Most selling happens with unreliable data and power.
- Slips are rarely wanted.

## Architecture decisions for Phase 1

Phase 1 runs as one deployable .NET 10 application made of modules that match the five planned services, so any module can later be split out without redesign.

| Decision | Choice for Phase 1 | Why |
| --- | --- | --- |
| Deployment shape | Modular monolith: one API app, plus one worker app for background jobs | A small team ships faster; module boundaries are the same as the future services |
| Modules | ShopCore, Catalogue, Marketplace (light), Messaging, Identity | Wallet comes in Phase 3 |
| Module rules | Each module has its own Domain, Application and Infrastructure projects and its own PostgreSQL schema; modules talk only through public contracts or domain events, never each other's tables | Keeps the split to microservices a deployment change, not a rewrite |
| Events between modules | In-process events now, through the transactional outbox; Azure Service Bus switched on when a module is split | Same code path either way |
| Database | One Azure Database for PostgreSQL 18 server, one schema per module | Cheaper; still clearly separated |
| Front end | One React PWA for the till (phone, tablet and PC layouts); back office in the same app behind owner permissions for Phase 1 | One codebase; separate apps later |
| Offline | PowerSync self-hosted, local SQLite on every device | Proven sync; writes upload to the ShopCore API |
| Identity | Microsoft Entra External ID; staff PINs checked on device and server | Managed sign-in, fast PIN at the till |
| Hosting | Azure South Africa North: Container Apps, PostgreSQL, Key Vault, Blob Storage, Application Insights | Local data, scales per app |

Each decision above gets its own Architecture Decision Record (ADR) in the repository in Sprint 0.

## Repository and solution structure

One GitHub repository holds backend, front end and infrastructure, so a feature and its tests ship in one pull request.

```
tillknowit/
  backend/
    TillKnowIT.sln
    src/
      Host/
        TillKnowIT.Api/                 # minimal API host, auth, OpenAPI, module registration
        TillKnowIT.Worker/              # background jobs: slips, summaries, invoice reading, reports
        TillKnowIT.AppHost/             # .NET Aspire: runs everything locally
      BuildingBlocks/
        TillKnowIT.SharedKernel/        # Entity, ValueObject, Result, DomainEvent, Money
        TillKnowIT.Dispatching/         # in-house command/query dispatcher
        TillKnowIT.Outbox/              # transactional outbox and event publisher
      Modules/
        ShopCore/
          ShopCore.Domain/
          ShopCore.Application/
          ShopCore.Infrastructure/      # EF Core, schema "shopcore", stored procedure calls
          ShopCore.Contracts/           # public commands, queries, events other modules may use
        Catalogue/  (same four projects)
        Marketplace/  (same four projects)
        Messaging/  (same four projects)
        Identity/  (same four projects)
    tests/
      <Module>.Domain.Tests/  <Module>.Application.Tests/
      TillKnowIT.Architecture.Tests/   # enforces layer and module rules
      TillKnowIT.Api.IntegrationTests/ # Testcontainers PostgreSQL
  frontend/
    apps/till/                         # React PWA: till + back office for Phase 1
    apps/field-agent/                  # setup checklist app
    packages/design-system/            # components, .module.css, tokens.css, Storybook
    packages/domain/                   # pricing, VAT, stock rules in TypeScript
    packages/api-client/               # generated from OpenAPI
    packages/sync/                     # PowerSync schema and connector
    packages/i18n/                     # en, zu, xh, st, tn, af
  database/
    functions/                         # report functions and procedures (.sql, versioned)
  infra/
    bicep/                             # Azure resources per environment
  docs/
    adr/                               # Architecture Decision Records
  .github/workflows/                   # build, test, scan, deploy
```

**Module rule:** a module may reference another module's `Contracts` project only. The architecture tests fail the build otherwise.

## Epics and user stories

Priority uses MoSCoW (Must, Should, Could). Size is a first guess: S is up to 2 days, M up to a week, L up to two weeks. Every story also meets the definition of done.

### E1 Accounts, shop setup and devices

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E1-1 | As an owner, I sign up with my phone number and create my shop | OTP sign-in; shop name, area, language; shop status "pending verification" until a field agent approves | Must | M |
| E1-2 | As an owner, I add staff with a role and a 4–6 digit PIN | Roles from the roles table; PIN stored hashed; staff can be disabled | Must | M |
| E1-3 | As an owner, I register a phone or PC as a till | Device gets an ID; owner approves; lost device can be switched off remotely and its local data cleared at next start | Must | M |
| E1-4 | As a cashier, I unlock the till with my PIN, even offline | PIN checked against the synced hash; 5 wrong tries lock for 5 minutes | Must | S |
| E1-5 | As a user with several memberships, I choose which one to open | Shown only when more than one membership | Should | S |

### E2 Catalogue and products

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E2-1 | As an owner, I scan a barcode and get the product name and photo from the shared catalogue | Match in under 1 second offline for synced catalogue; unknown barcode opens a short form | Must | M |
| E2-2 | As an owner, I add or edit a product: name, barcode, category, cost, price, low-stock level, expiry | Categories from lookup table; price suggestion from markup; duplicate barcode blocked | Must | M |
| E2-3 | As a platform admin, I approve new products into the shared catalogue | Queue of shop-added products; approve, merge or reject | Should | M |

### E3 Selling (offline)

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E3-1 | As a cashier, I add items by tile, search or scan | Tiles sorted by most sold (30 days); USB scanner and camera scanning; works with no network | Must | L |
| E3-2 | As a cashier, I take payment by cash, card, QR or credit book | Cash shows change and quick notes; card and QR record the method only; credit book picks or creates an account | Must | M |
| E3-3 | As a cashier, sale completes in under 1 second offline | Sale, stock movements and outbox entry written in one local transaction | Must | M |
| E3-4 | As a manager, I cancel a sale with my PIN | Creates a cancellation event; stock and credit balance restored; logged | Must | S |
| E3-5 | As a cashier, I'm blocked from selling expired items | Block with reason; manager override logged | Should | S |
| E3-6 | As a cashier, I give a slip only if the customer asks | "Slip?" button: QR on screen (free), or WhatsApp, SMS, email; can be sent later from sale history | Must | M |

### E4 Stock in

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E4-1 | As an owner, I receive stock by scanning items | Each scan adds one; quantity, cost and price editable; saved as one receive movement with supplier and invoice number | Must | M |
| E4-2 | As an owner, I upload a supplier's Excel or CSV | Column matching with saved mappings per supplier; units per case; review table before saving | Should | M |
| E4-3 | As an owner, I take a photo of an invoice or upload a PDF and the items are read for me | Queued offline, read when online; review table shows matches, case sizes and cost per unit; nothing saved until confirmed | Must | L |

### E5 Stock management

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E5-1 | As an owner, I see my stock with filters: running low, out, not selling, expiring | Stock calculated from movements; filters use shared specification rules | Must | M |
| E5-2 | As an owner, I count stock and record a reason for each difference | Reasons from lookup table (counted, expired, damaged, missing, own use) | Must | S |

### E6 Credit book and E7 Cash-up

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E6-1 | As an owner, I see who owes what and record payments | Balance from entries; history per customer; optional credit limit with warning | Must | M |
| E7-1 | As a cashier, I cash up at the end of the day | Float + cash sales + credit cash payments = expected; enter counted; over or short saved per till per day | Must | M |

### E8 Insights and reports

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E8-1 | As an owner, I see sales, profit, customers and average sale for today, 7 and 30 days | Works offline from local data for the last 90 days | Must | M |
| E8-2 | As an owner, I see best sellers, running low with days left, not selling, expiring | Suggested order quantity for two weeks | Must | M |
| E8-3 | As an owner, I download reports as PDF, Excel or CSV | Reports from database functions; large reports run in the background with a download link | Should | L |

### E9 Compliance, E10 Airtime and electricity, E11 Daily summary and loss alerts

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E9-1 | As an owner, I store my licence, permit and certificates with expiry dates | Photos in encrypted storage; reminders 30 and 7 days before expiry | Should | M |
| E10-1 | As a cashier, I sell airtime, data or electricity | Through one licensed partner; needs network; clear message when offline; commission recorded | Should | L |
| E11-1 | As an owner, I get an evening summary | Push or email: sales, profit, low stock, expiring, cash-up result, cancellations | Should | S |
| E11-2 | As an owner, I'm alerted to unusual activity | Rules from settings table, such as 3+ cancellations in an hour or cash-up short above a set amount | Should | M |

### E12 Languages, E13 Field agent app, E14 Founding suppliers

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| E12-1 | As a user, I use the app in my language | English, isiZulu, isiXhosa, Sesotho, Setswana, Afrikaans; per-user choice; no hard-coded text | Must | M |
| E13-1 | As a field agent, I set up a shop with a checklist | Device check, shop details, document photos, catalogue import, staff PINs, test sale, training sign-off; verifies the shop | Must | M |
| E14-1 | As a founding supplier, I list my catalogue and receive orders | Catalogue upload; orders by app, WhatsApp or email; confirm or change lines | Could | L |
| E14-2 | As an owner, I see my usual supplier's price when stock runs low and place an order | Order received into stock with one tap on delivery | Could | M |

## Phase 1 data model

All IDs are UUID v7 (time-ordered, created on the device so offline records never clash). Money is stored in cents as whole numbers. Every table has `created_at`, `created_by`, `device_id` and, where editable, `updated_at` and `version`. Lookups follow one shape: `code`, `names` (per language), `is_active`, `sort_order`.

| Schema | Table | Key columns | Notes |
| --- | --- | --- | --- |
| identity | people | id, phone, email, preferred\_language | One per human |
| identity | organisations | id, type (platform, shop, supplier), name, status | Status: pending, verified, suspended |
| identity | memberships | person\_id, organisation\_id, role\_id, status, pin\_hash | PIN for staff at tills |
| identity | roles, permissions, role\_permissions | code, names | Permissions as data, such as sale.cancel |
| identity | devices | id, shop\_id, name, status, last\_seen\_at | Remote wipe flag |
| catalogue | catalogue\_products | id, barcode, name, size, category\_code, photo\_url, status | Shared master list |
| shopcore | products | id, shop\_id, catalogue\_product\_id, name, barcode, category\_code, cost\_cents, price\_cents, low\_stock\_level, expiry\_date, is\_active | Shop's own copy and prices |
| shopcore | stock\_movements | id, shop\_id, product\_id, type (receive, sale, cancel, count, write\_off), quantity, unit\_cost\_cents, reason\_code, supplier\_name, invoice\_ref | Append only; stock = sum |
| shopcore | sales | id, shop\_id, till\_device\_id, staff\_id, total\_cents, vat\_cents, payment\_method\_code, tendered\_cents, change\_cents, account\_id, sold\_at | Append only |
| shopcore | sale\_lines | sale\_id, product\_id, name, quantity, price\_cents, cost\_cents | Name and price copied at time of sale |
| shopcore | sale\_cancellations | sale\_id, staff\_id, reason, cancelled\_at | Append only |
| shopcore | slip\_requests | sale\_id, channel (qr, whatsapp, sms, email), destination, status | Only when a customer asks |
| shopcore | credit\_accounts | id, shop\_id, name, phone, credit\_limit\_cents | Balance = sum of entries |
| shopcore | credit\_entries | account\_id, type (sale, payment, cancel), amount\_cents, sale\_id | Append only |
| shopcore | cash\_ups | shop\_id, device\_id, day, float\_cents, expected\_cents, counted\_cents, difference\_cents, note | One per till per day |
| shopcore | compliance\_documents | shop\_id, type\_code, file\_url, expires\_on | Encrypted blob storage |
| shopcore | shop\_settings | shop\_id, key, value | Markup, alert levels, float, slip footer |
| shopcore | invoice\_imports | id, shop\_id, file\_url, status, result\_json | Queued and read by the worker |
| shopcore | vas\_sales | id, shop\_id, product\_type (airtime, data, electricity), amount\_cents, partner\_ref, commission\_cents, status | Online only |
| marketplace | suppliers, supplier\_products, purchase\_orders, purchase\_order\_lines | as in the product spec | Founding suppliers only |
| messaging | message\_templates, outbound\_messages | code, language, channel, body; status, cost\_cents | Templates as data |
| platform | lookups (categories, units, payment\_methods, vat\_rates, adjustment\_reasons, document\_types, languages), feature\_flags, audit\_log |  | Managed in admin screens |

## API endpoints

REST over HTTPS, versioned under `/api/v1`, described in OpenAPI and generated into the front-end API client. Every write accepts an `Idempotency-Key` header (the record's UUID) so a retried upload never creates a duplicate. Errors use RFC 9457 problem details.

| Module | Method and path | Purpose |
| --- | --- | --- |
| Identity | POST /auth/otp, POST /auth/verify | Phone sign-in |
| Identity | GET /me, GET /me/memberships | Current person and places they belong to |
| Identity | POST /shops | Owner creates a shop |
| Identity | POST /shops/{id}/staff, PATCH /staff/{id} | Add, edit, disable staff and PINs |
| Identity | POST /shops/{id}/devices, POST /devices/{id}/wipe | Register and switch off tills |
| Catalogue | GET /catalogue/products?barcode= | Look up the shared catalogue |
| Catalogue | POST /catalogue/suggestions | Shop proposes a new product |
| ShopCore | POST /sync/upload | PowerSync upload: batch of sales, movements, entries, cash-ups from the device |
| ShopCore | POST /products, PATCH /products/{id} | Shop products |
| ShopCore | POST /sales/{id}/slip | Send a slip on request (channel, destination) |
| ShopCore | POST /stock/imports (file) | Excel, CSV, PDF or photo; returns an import ID |
| ShopCore | GET /stock/imports/{id}, POST /stock/imports/{id}/confirm | Review and confirm read items |
| ShopCore | POST /compliance/documents | Upload a licence or certificate |
| ShopCore | POST /vas/sales | Airtime, data or electricity (online) |
| ShopCore | GET /reports/{code}?from=&to=&format=pdf\|xlsx\|csv | Run a report; large ones return 202 and a job ID |
| Marketplace | GET /suppliers, GET /suppliers/{id}/products | Founding suppliers and prices |
| Marketplace | POST /purchase-orders, POST /purchase-orders/{id}/receive | Order and receive into stock |
| Platform | GET /lookups/{type}, admin CRUD under /admin/lookups | Lookup tables |
| Platform | GET /jobs/{id} | Status and download link for background jobs |

## Offline sync rules

Each device holds only its own shop's data plus the shared lookups and catalogue; every write is saved locally first and uploaded through `POST /sync/upload`.

| Data | Goes to | Direction | Conflict rule |
| --- | --- | --- | --- |
| Lookups, templates, feature flags | Every device | Server → device | Server wins |
| Shared catalogue (barcode, name, photo URL) | Every device in pilot areas | Server → device | Server wins |
| Shop products and settings | Devices of that shop | Both ways | Last change wins, by `updated_at`; change kept in audit log |
| Staff memberships and PIN hashes | Devices of that shop | Server → device | Server wins |
| Sales, sale lines, cancellations, stock movements, credit entries | Devices of that shop (last 90 days) | Device → server, then to the shop's other devices | Append only: no conflicts; duplicates ignored by ID |
| Cash-ups | The device that made it, plus owner devices | Device → server | One per till per day |
| Reports beyond 90 days, invoice images, documents | Not synced | Fetched online on demand | — |

**Rules**

- Stock on hand and credit balances are recalculated on the device from synced movements and entries after every sync.
- Negative stock is allowed and flagged for a count.
- Upload batches of up to 500 records; retry with backoff (5 s, 30 s, 2 min, up to 10 min).
- The server validates every uploaded record (shop, device, staff and permission) before accepting it; rejected records are returned with a reason and shown to the owner.
- A device that hasn't synced for 24 hours shows a red status; the owner is alerted at 48 hours.

## Non-functional acceptance criteria

These are tested on the reference budget phone (Android 8+, 1–2 GB RAM, Chrome) as well as a Windows 10 PC.

| Area | Must pass |
| --- | --- |
| Speed | Add item under 200 ms; complete sale under 1 s offline; app opens under 3 s after first install |
| Install size | PWA download under 5 MB; local data for a typical shop under 100 MB |
| Offline | 7 days offline with 300 sales a day, then full sync with no lost or duplicate records |
| Sync | 1,000 queued records upload in under 2 minutes on 3G |
| Data use | Under 2 MB per day of sync for a busy shop |
| Security | OWASP ASVS level 2 checks; no high or critical findings in scans; row-level security tests pass for every table |
| Accessibility | WCAG 2.2 AA; touch targets at least 48 px; works at 200% text size |
| Languages | No untranslated text in any of the six languages |
| Availability | API 99.5% monthly during the pilot; till unaffected by API downtime |
| Recovery | At most 15 minutes of server data lost; service back within 4 hours |

## Sprint plan

Two-week sprints, about 16 weeks to a pilot-ready Phase 1 with a team of 5–6 developers. Dates are set once the team starts; the order matters more than the dates.

1. **Sprint 0 – Foundations and experiments:** repository, CI/CD, Azure environments, Aspire, ADRs; walking skeleton (one product saved on a phone, synced, shown on a PC); PowerSync test on a budget phone offline for a day; invoice reading tested on 20 real invoices.
2. **Sprint 1 – Identity and setup:** E1-1 to E1-4, roles and permissions as data, lookups and admin screens, design system basics, i18n set up.
3. **Sprint 2 – Products and catalogue:** E2-1, E2-2, catalogue seed, barcode scanning (camera and USB), phone and PC layouts.
4. **Sprint 3 – Selling offline:** E3-1 to E3-4, local transactions, outbox, sync upload, cancellation.
5. **Sprint 4 – Stock in and stock:** E4-1, E4-3, E5-1, E5-2, invoice import queue and review.
6. **Sprint 5 – Money:** E6-1, E7-1, E3-5, E3-6 (slips on request), E4-2 (Excel).
7. **Sprint 6 – Insights and reports:** E8-1 to E8-3, report functions, PDF and Excel export, E11-1.
8. **Sprint 7 – Spaza extras:** E9-1, E10-1, E11-2, E13-1 (field agent app), E12-1 translations complete.
9. **Sprint 8 – Hardening and pilot prep:** performance and offline tests, security review and penetration test, bug fixing, training material; E14 founding suppliers if time allows.

After Sprint 8: install in the first 5 pilot shops, fix for two weeks, then roll out to the remaining 15–25.

## Ways of working

**Environments**

| Environment | Purpose | Deployed |
| --- | --- | --- |
| Local | Each developer, through .NET Aspire with PostgreSQL in a container | Any time |
| Development | Shared integration testing, fake partners | Every merge to main |
| Staging | Same as production, test partner accounts, pilot rehearsal | Release candidates, after tests pass |
| Production | Pilot shops | Approved releases, outside trading hours |

**Branching and releases**

- Trunk-based: short-lived feature branches (under 3 days) merged to `main` by pull request.
- Every pull request: one reviewer, all tests, analysers, architecture tests, security scans and a preview build must pass.
- Feature flags hide unfinished work instead of long-lived branches.
- Database changes through EF Core migrations and versioned SQL files only; never by hand.
- Commit messages follow Conventional Commits, so release notes are generated.

**Definition of ready (before a story enters a sprint)**

- [ ] Story and acceptance criteria written and agreed
- [ ] Screens designed for phone and PC, if it has a screen
- [ ] API contract drafted, if it needs one
- [ ] Lookups, permissions and translations it needs are listed
- [ ] Sized by the team

**Definition of done**

- [ ] Acceptance criteria met and demonstrated
- [ ] Unit, integration and, where relevant, offline end-to-end tests pass
- [ ] Code reviewed; no analyser warnings; architecture tests pass
- [ ] Works offline where the story says so, on the reference budget phone
- [ ] Text in all six languages; accessible (WCAG 2.2 AA)
- [ ] API documented in OpenAPI; ADR written if a decision was made
- [ ] Deployed to staging and checked by the product owner
