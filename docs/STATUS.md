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
| M6-1 to M6-3 Group buying | Done | Supplier posts a group deal (price, minimum, closing date, area); shops in the area join with a quantity and see only the total and their own quantity; at the deadline it is either one sent order per shop at the bulk price (supplier terms and delivery fee apply) or everyone is told and nobody is charged; supplier can close early once the minimum is met or cancel. Closing is safe to re-run. 500 commitments close well under a minute (tested). |
| M6-4 Invite-only groups | Todo | Could. |
| M7-1 Rate a delivery | Done | On time, complete, 1 to 5, one per order; a supplier average counts in offer ranking only after 3 ratings. |
| M7-2 Demand totals | Done | Counts per area and product of opted-in shops low on the supplier's items; counts under 3 are never shown; no names, sales or stock levels (tested). Shops that never chose to share are not even queried. Calls each shop in a loop for now: batch it if areas get large. |
| Marketplace as its own service, Service Bus | Decided not yet | See ADR 0002: same hosts, same boundaries; split when there is a reason. |
| Supplier portal app | Todo | Front-end only; the API for it is all there. |

## Phase 3: school wallet

Backend only: the screens (parent app, till wallet payment, photo check) and the NFC/Capacitor work are not built. Everything below is tested without a database or a partner.

| Story | State | Notes |
| --- | --- | --- |
| Double-entry ledger | Done | Accounts, append-only transactions, balanced legs (the domain refuses anything that does not add to zero; a 2,000-case property test covers every posting kind). Balances are calculated, never stored. A unique reference per transaction makes every request safe to repeat. A wallet's version moves on every debit so two simultaneous spends cannot both win. `backend/scripts/sql/wallet-ledger-checks.sql` adds views for spot checks; there is no trigger (the file says why). |
| W1-1 Schools | Done | The platform registers and verifies a school against a verified tuckshop; school admins; settings (payout schedule, daily ceiling, staff accounts, offline split minutes). Only the platform can change the fee. |
| W1-2 Import learners and teachers | Done | CSV or Excel with header aliases; duplicates flagged within the file and against the list; bad rows reported; safe to send again. Each new person gets a wallet and a one-time link code to print. Photos (JPEG or PNG) are stored encrypted and served only to a till of that school. |
| W1-3 Cards and wristbands | Done | The NFC UID is stored as a hash; one card, one person; a blocked card stays blocked. Port: the NFC reading itself (Capacitor plugin, USB readers) is front-end work. |
| W2-1 Parent links a child | Done | Link code plus consent (version recorded) before anything about the child is shown; up to 4 parents per child. |
| W2-2 Top-ups | Done (code) | Card or instant EFT through the partner's checkout, credited only on a signed, replay-protected, once-only partner callback. Cash at the school office is recorded by the admin (the school owes that cash until it is paid in). Port: the licensed stored-value partner (only a Development fake exists). |
| W2-3 Limits and blocked items | Done | A daily limit and blocked products or categories. The school's ceiling and the parent's limit both apply; the lower wins. |
| W2-4 Alerts and statement | Done | Optional push on every purchase; one low-balance alert per crossing (push, and WhatsApp if opted in); statement with a running balance as JSON, CSV, Excel or PDF. Messaging never blocks or fails a sale. |
| W2-5 Automatic weekly top-up | Todo | Could. Needs a saved card at the partner. |
| W2-6 Transfers between children | Done | Only between a parent's own children; once per reference. |
| W3-1 Pay at the till | Done | NFC card or rotating QR (60 seconds, signed). The cashier gets name, grade, class and a photo marker. Limits, blocked items and balance are checked; idempotent by sale id; the platform fee is split out in the ledger. |
| W3-2 Offline wallet | Done | Each till gets a signed allowance per wallet (lower of the balance and what is left of today's limit) and never the real balance. Settlement verifies the signature, wallet, school and day, and caps the day's offline sales across all tills at the allowance; sales over it are rejected and the shop owner and parent are told. A sale inside the allowance is honoured even if the wallet has since been emptied elsewhere: the shop is paid and the wallet shows overdrawn (the parent is told). Whole-day outage test: 40 learners, up to 600 sales over two tills, nobody over their allowance, ledger balanced. |
| W3-3 Block a lost card | Done | A parent or the school blocks it; the block reaches tills in the next snapshot; a replacement links to the same wallet. |
| W4-1 Teachers | Done | A teacher wallet, or a monthly staff account if the school turns it on; monthly statement per teacher; settlement posted once per teacher and month. |
| W4-2 and W4-3 Pre-orders | Done | Breaks with cut-off times (South African time); a menu set by the school; prices come from the menu; paid from the wallet when placed; cancellable until the cut-off with the exact amount and fee returned; the cashier lists by break and class and marks collected; today's paid orders are in the till snapshot. |
| W5-1 Payouts | Done (code) | Schedule-driven (daily, weekly on Friday, monthly on the last day), once per shop per day, the partner reference is the idempotency key; a failed payout is recorded and retried; the owner sees sales, fees, payouts and what is owed. Port: the partner payout call. |
| W5-2 Refund on leaving | Done (code) | The parent asks once the child is marked as left; the school confirms; the partner is asked first, so a failure changes nothing. |
| W5-3 Reconciliation | Done | A daily job compares the ledger with the partner's own total to the cent and checks that every transaction balances; any difference blocks all payouts until a platform admin writes a resolution. |
| W5-4 Erasure | Done | Names, grade, class, photo, link code and guardian links are removed and cards blocked; the ledger is kept (it holds no names). A wallet with money waits for the refund, then completes; due within 30 days. |
| Separate wallet database and service | Prepared | `ConnectionStrings:Wallet` puts the module in a database of its own today. The separate Container App, Key Vault and network rules are infrastructure work: see ADR 0003. |
| Parent app, till screens, native NFC, penetration test | Todo | Front-end and infrastructure work; the penetration test and four-eyes reviews are process items. |

## Phase 4: every business

Backend and engines. The screens, hardware adapters and kitchen view are front-end work; rows are updated as each part lands. Every feature below is switched on by the shop's business pack: a shop on the spaza pack gets "feature.not_enabled" from all of them.

| Story | State | Notes |
| --- | --- | --- |
| B1-1 Business packs as data | Done | New Platform module: packs with versioned features and settings, 23 known feature flags, five seeded packs (spaza, cafe, butchery, salon, clothing and hardware). A platform admin creates, edits (each edit is a new version) and retires packs; the default pack is never retired. A brand-new pack works with no code change (tested). |
| B1-2 Owner chooses a business type | Done | Only the owner; every switch recorded; no product, sale or customer is touched, so switching back restores everything. `IShopFeatures` answers "is this on?" for other modules. |
| Core stays clean | Done | Architecture tests: the selling core cannot depend on the Platform module (features come through ShopCore's own `IFeatureGate` port) and no pack code may appear as a string anywhere in ShopCore's domain or application code. |
| B2-1 Variants | Done | A variant is a product of its own under a parent (own barcode, price, cost and stock), so the sale flow and stock needed nothing new; labels and barcodes cannot repeat; a variant follows its parent's way of being sold. |
| B2-2 Modifiers | Done | Required and optional groups with minimum and maximum; each option is a product (the add-on), so its price and stock are managed like any product; the server prices a choice against the product's groups. |
| B2-3 Promotions and combos | Done | Percent, amount off, fixed price and combos with days, hours (South African time, past midnight too) and dates; no stacking; the engine runs on the server for previews and a till downloads the promotions to run it locally; per-line discounts add up exactly (property-tested over 500 random baskets). A sale line's discount must be a promotion or a manager's price override, and an override must come from a role that allows it. |
| B2-4 Refunds | Done | Never more than was sold less earlier refunds, pro rata on what was actually paid; a manager above a threshold (default R500, the shop can set it); the goods go back to stock (Return movement); money goes back the way it came (credit sales reduce the debt); wallet, voucher and lay-by sales are refunded online, where a voucher can be given. Synced from a till or made online; both paths share one set of rules. Cash refunds come out of the expected cash at cash-up. |
| B2-5 Split payments | Done | Parts add up to the bill plus tip, each method once, change worked out by the server, wallet and voucher parts carry their reference; credit cannot be one of several parts. The cash part counts at cash-up. |
| B2-6 Open tickets | Done | A till holds a ticket while editing; others see it locked; the hold lapses after 10 minutes; merge, move table, close into a sale, void. |
| B3-1 Tables | Done | Floor plan replaced by area and name so tables keep their ids; removed tables are retired, never deleted. |
| B3-2 Kitchen | Done (server) | Orders with per-till order numbers, new, preparing, ready, collected; a list the kitchen screen polls. The kitchen view itself and the 3-second target are front-end and network work. |
| B3-3 Tips | Done | On top of the bill, by amount or percentage, never more than the bill; in the cash drawer, not in sales figures. Tips report per staff: Todo. |
| B4-1 Weight | Done | Price per kg, quantity in grams, tare, half-up rounding; a generic unit divisor in the sale arithmetic, used by insights, reports and slips (0.700 kg). Needs a scale adapter in the front end. |
| B4-2 Price-embedded barcodes | Done | Rules from the pack settings, EAN-13 check digit, weight or price; ordinary barcodes are left to the normal lookup. |
| B6-1 Quotes | Done | Total worked out on the server, valid up to 90 days, PDF, one tap to an open ticket, expiry. |
| B6-2 Lay-by | Done | Deposit minimum, maximum term, cancellation fee and minimum fee from the pack settings; payments appended; stock reserved while active or waiting; cancel or expire gives back what was paid less the fee; collected goods are an ordinary sale paid by lay-by; an hourly job expires overdue plans. Lay-by cash counts at cash-up. |
| B6-3 Serial numbers | Done | Flag on the product; recorded against the sale, never more than were sold, a serial is sold once; searchable. The till enforces entering them. |
| B7-3 Gift vouchers | Done | Code shown once and only its hash kept; balance from entries; partial redemption; expiry from the pack settings; a repeated redemption takes nothing twice; cash vouchers count at cash-up. Redemption needs a connection. |
| B7-1 Customers | Done | Customers module: SA phone normalisation, consent, directory; sale events feed it. |
| B5 Appointments and commission | Done | Bookings module: services, staff, idempotent booking, clash refused online and flagged when booked offline, reschedule, consent-only WhatsApp reminders (24h, worker), commission rules (most specific wins, half-up) and report. 28 tests. |
| B7-2 Loyalty | Done | Append-only points ledger, earn on sale, reversed on cancel/refund. 40 tests. |
| B8-1, B8-2 Printer and cash drawer | Todo | Hardware package (front end). |
| B8-3 Message bundles | Todo | Messaging. |

## Phase 5

| Item | Status | Notes |
| --- | --- | --- |
| G3-1 Plans | Done | Billing module: plans and features as data; seeded shop and supplier plans with PLACEHOLDER prices until the pilot pricing decision. |
| G3-2 Subscribe, upgrade, downgrade, cancel | Done | Upgrade pays the pro-rata difference now (declined = no change); downgrade and cancel at period end; invoice with VAT emailed. |
| G3-3 Sponsored campaigns | Done | Budget, areas, dates; showings billed by use and capped at the budget; always labelled sponsored. Marketplace does not show them yet. |
| G3-4 Failed payments | Done | Retry, reminders on set days, extras limited after grace; the till is never blocked. Real gateway not connected (dev charger only). |
| G1-1, G1-4 Branches | Done | A branch is a shop under a head shop (one level), needs the multi_branch plan feature; owner and general managers (all-branches scope) see every branch, branch staff only their own. |
| G1-2 Stock transfers | Done (online) | Out movement on send, in on receive, shortfall shown, cancel while in transit, idempotent. Offline upload of transfers is not built: needs the sync path. |
| G1-3 Combined reports | Done | `branch=all` or a branch id on every report; needs report.view on the business. Product reports merge products by name across branches. |
| G2 Accounting export | Todo | |
| G4-1 Pay-later consent | Done | Finance module: consent per named partner, withdrawable (nothing shared once withdrawn), every share logged and shown to the owner. We never lend: the partner decides. Only a dev partner exists; no real provider or agreement yet. |
| G4-2 Pay an order with terms | Done | Partner pays the supplier (port), order marked paid, repayments tracked from signed partner callbacks (idempotent), credit comes back as repayments arrive. |
| G5 Insights | Todo | |
| G6 Languages, groups, partner API | Todo | |
