# Build status

Updated with every commit. Legend:

- **Done**: rules, server endpoints and tests are in; where the story has a screen, the screen is not counted unless it says so.
- **Rules**: business rules and tests are in (C# and, where the till needs them offline, TypeScript). No screen and/or no endpoint yet.
- **Port**: built up to an interface with a fake for tests; the real outside service is not connected.
- **Blocked**: cannot be finished without a decision, account or partner. The reason is given.
- **Todo**: not started.

"Tests" here means automated tests that run without a database. Nothing has yet been run against a real SQL Server, because Docker is not running
on the build machine. The first real run is the first item on the hand-over list at the bottom.

## Phase 1: spaza till

| Story | State | Notes |
| --- | --- | --- |
| E1-1 Sign up, create shop | Partly | Create shop (pending until verified) done. OTP sign-in blocked on Entra External ID and an SMS provider. |
| E1-2 Staff and PINs | Done | PBKDF2 PINs, add/update/disable. |
| E1-3 Devices | Done | Register, approve, wipe, check-in. |
| E1-4 PIN unlock offline | Done | Server check and till screen, lockout 5 tries / 5 minutes. |
| E1-5 Choose membership | Done | Memberships listed per person (till shows the choice only when more than one). |
| E2-1 Barcode lookup | Done | Catalogue lookup and suggestions. |
| E2-2 Add/edit product | Done | Sync batch and REST, last change wins. |
| E2-3 Approve catalogue products | Done | Review queue: approve (with tidy-ups), reject (reason required) or merge (shops move to the approved product). Admin lookup management with translations; everything audited. |
| E3-1 to E3-4 Selling, payment, cancel | Rules | Server upload and till rules done. The selling screen and local database are blocked on the PowerSync test against SQL Server (ADR 0001). |
| E3-5 Expired items blocked | Done | Rule on the till; a manager override is kept on the sale line and the server checks the manager's role allows it. |
| E3-6 Slips on request | Done | QR (signed link, free), WhatsApp, SMS and email through the Messaging module; at most 3 per sale; public slip page shows no staff or customer details. QR image drawing is a till screen. WhatsApp, SMS and email providers are not connected (Port, see Messaging below). |
| E4-1 Receive stock | Rules | |
| E4-2 Excel/CSV import | Done | Reads .xlsx and .csv (semicolons, comma decimals, Latin-1 handled), guesses columns, remembers the choice per supplier, works cases into units and case price into cost per unit, matches by barcode then name with suggestions, review table, nothing saved until confirmed. |
| E4-3 Invoice reading | Port | Photo/PDF upload, queueing, the review and confirm flow and the worker are done. The reader itself (OCR or a language model) is not connected: queued invoices fail with a clear message, and a development stand-in exists for trying the flow. Spec asks for a test on 20 real invoices. |
| E5-1, E5-2 Stock list, counts | Done | |
| E6-1 Credit book | Done | |
| E7-1 Cash-up | Done | |
| E8-1, E8-2 Insights | Done | Server endpoint and the same rules in TypeScript for offline use. Screens not built. |
| E8-3 Reports and export | Partly | Five reports as CSV, Excel and PDF (own PDF writer, no library licence). Reports are LINQ queries, not T-SQL functions, and run immediately; background jobs for very large reports are not built. |
| E9-1 Compliance documents | Done | Upload (JPEG/PNG/PDF, 10 MB, content checked), AES-256-GCM encrypted storage, list with status, download, delete, and WhatsApp/email reminders at 30 days, 7 days and expiry, sent once. Local encrypted disk store; the Azure Blob adapter is not written. |
| E10-1 Airtime and electricity | Port | Sale flow, validation, limits, commission and the safety rules are done: the sale is saved before the partner is called, a repeat never buys twice, a no-answer is recorded as unknown and never retried. The partner adapter is not connected (needs a licensed partner); a fake exists in Development only, elsewhere the feature reports "not available". Limits and commission rates are placeholders. Reconciliation of unknown sales is not built. |
| E11-1 Daily summary | Done | Sent once a day after 19:00 shop time to every verified shop, by WhatsApp or email. |
| E11-2 Loss alerts | Done | Cancellations per hour and short cash-ups, levels from shop settings with defaults; plus a 48-hour offline-till alert. All sent once (dedupe keys). Push needs the owner app; WhatsApp/SMS/email providers are not connected. |
| E12-1 Six languages | Partly | Structure and English done; translations need translators. |
| E13-1 Field agent setup | Partly | The seven-step checklist, step completion, and verify (all steps needed, permission checked) are done on the server. The field agent app itself is not built. |
| E14-1, E14-2 Founding suppliers | Todo | |

## Phase 2: supplier marketplace

| Story | State | Notes |
| --- | --- | --- |
| M1-1 Supplier applies | Done | Business details, registration number shape, contacts, delivery areas; pending until verified. |
| M1-2 Verify or reject | Done | Four-item checklist, all required; rejection needs a reason; verified supplier's organisation becomes verified. |
| M1-3 Invite reps | Done | Reps get the supplier-rep role; only someone allowed in that supplier can invite. |
| M2-1 Supplier catalogue upload | Done | Excel/CSV with column matching, preview then apply, matched to the shared catalogue by barcode, case size, case price, unit price, VAT flag. |
| M2-2 Areas, days, minimum order, fee | Done | Areas from the municipalities lookup (the eight metros are seeded; admins add more). |
| M2-3 Deals | Done | Discount, free goods and combo deals with dates and areas the supplier serves; sponsored deals are labelled; browse by area. |
| M3-1 Reorder options | Done | Ranked options per product and an order list: true cost per unit (VAT, deal, delivery), usual supplier first with last price, cheapest marked, margin at the shop's price, ranking weights in settings. Fairness test: with default settings a sponsored offer never outranks a cheaper non-sponsored one (500 random cases). Ratings are not in the ranking yet (M7). |
| M3-2 Sharing settings | Done | Off, usual or all; off until the owner chooses. Nothing reads it yet: it gates the demand totals (M7-2). |
| M3-3 Suggested replacement | Todo | Could. |
| M4-1 to M4-4 Ordering | Done | Create, build from order list, send (app, WhatsApp, email), supplier replies per line, delivery, shop receive (count wins), last prices, usual supplier, prefilled stock-in review. Port: real WhatsApp provider |
| M5-1 to M5-4 Paying suppliers | Done (code) | Pay in app or on delivery (supplier marks cash paid), hold until delivery, release on shop confirmation or after 72h, short deliveries refund the difference, disputes freeze money until an admin resolves, signed webhooks with replay protection, audited states, daily reconciliation to the cent, supplier statement as CSV/Excel/PDF. Port: real payment partner (only a Development fake exists; webhook secret per partner in config). Not atomic across modules: a failed order-status update after money moves is caught by reconciliation. |
| M6-1 to M6-4 Group buying | Todo | |
| M7-1, M7-2 Ratings and demand | Todo | |
| Marketplace as its own service, Service Bus | Todo | |
| Supplier portal app | Todo | |

## Phases 3 to 5

Not started. These tables are filled in as each phase is built.

## Hand-over list

1. Start Docker and run the API and Worker against SQL Server: confirms migrations apply, seed loads, the outbox claim query works, and cascade paths are accepted.
2. Run the PowerSync-on-SQL-Server spike from ADR 0001 before building the local database and selling screen.
3. Decide OTP sign-in (Entra External ID) so endpoint authentication can replace the TODOs.
