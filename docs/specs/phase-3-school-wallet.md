# TillKnowIT – Phase 3 Development Spec: School Wallet

26 October 2026

## Scope of Phase 3

Phase 3 adds a cashless school tuckshop: parents load a wallet, learners and teachers pay with a QR code, card or wristband, and the tuckshop till accepts it even offline. It builds on Phase 1, Phase 2 and the TillKnowIT product specification.

**Start when**

- A licensed payment partner that can hold parents' money (stored value) is signed.
- 2–3 pilot schools have agreed, with a named school admin each.
- POPIA consent process for learners' data is approved by the information officer.

**In Phase 3**

- School onboarding: school admin, grades, learners, teachers, cards
- Parent app: sign up, link children with a school code, top up, limits, blocked items, notifications, statements
- Learner payment by QR (phone), NFC card or wristband, with photo check at the till
- Teacher wallets or monthly staff accounts
- Pre-orders for break time
- Offline wallet sales at the till within daily limits
- Refunds of unused balances when a learner leaves
- School tuckshop business pack switched on

**Not in Phase 3**

- Other business packs, printers, loyalty (Phase 4); bursaries or school-fee payments.

## Architecture decisions for Phase 3

Wallet money is the most sensitive data in the system, so Wallet and payments becomes its own service with the strictest security and audit.

| Decision | Choice | Why |
| --- | --- | --- |
| Wallet service | Wallet and payments split out as its own Container App, own database (not only own schema), separate Key Vault and admin access | Isolates money from everything else; smaller audit scope |
| Who holds the money | Licensed payment partner holds parents' funds; our ledger mirrors the partner's balances and reconciles daily | We never hold customer money ourselves |
| Ledger | Double-entry ledger: every top-up, purchase, refund and transfer is two balanced entries; balances calculated, never edited | Money always adds up and can be audited |
| Parent app | New front-end app `apps/wallet`, PWA, online-first with cached balance and history | Parents use their own phones; no app-store install needed |
| Learner payment tokens | Rotating QR codes (signed, short-lived) for phones; NFC card or wristband UID linked to the learner | QR screenshots can't be reused; cards work for young learners without phones |
| NFC reading | Android tills through Capacitor native NFC plugin (Web NFC where Chrome supports it); USB NFC readers on PCs | Web NFC is not on iPhone or every browser |
| Offline spending | Signed daily allowance per learner cached on the school's tills; offline sales deducted from it and settled at sync | Tuckshop keeps selling during outages without overspending |
| Notifications | Push first (free), WhatsApp only for low balance if the parent opts in | Keeps messaging costs low |

## Repository and module changes

```
backend/src/
  Host/
    TillKnowIT.Wallet.Api/            # new: Wallet and payments service
  Modules/
    Wallet/                           # new: schools, holders, guardians, wallets, ledger, pre-orders
      Wallet.Domain/ Wallet.Application/ Wallet.Infrastructure/ Wallet.Contracts/
    Payments/                         # moves into the Wallet and payments service
frontend/
  apps/wallet/                        # new: parent, teacher and older-learner app
  apps/till/                          # adds wallet payment, photo check, pre-order pick-up
  packages/nfc/                       # new: Capacitor NFC plugin wrapper + USB reader support
  apps/till-native/                   # new: Capacitor shell for Android tills that need NFC
infra/bicep/
  wallet.bicep                        # separate database, Key Vault and network rules
```

## Epics and user stories

### W1 School onboarding

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| W1-1 | As a platform admin, I register and verify a school and its tuckshop | School linked to a shop; school admin invited | Must | M |
| W1-2 | As a school admin, I import learners and teachers | Excel or CSV: name, grade, class; photo optional; duplicates flagged | Must | M |
| W1-3 | As a school admin, I issue a card or wristband to a learner | Tap to link the NFC UID; print a link code for the parent | Must | M |

### W2 Parent app

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| W2-1 | As a parent, I sign up and link my child with the school's code | Consent for the child's data recorded; several children allowed | Must | M |
| W2-2 | As a parent, I top up a wallet | Card or instant EFT through the partner; cash at the school office recorded by the admin | Must | L |
| W2-3 | As a parent, I set a daily limit and block items or categories | Applied at the next sync of the school's tills | Must | M |
| W2-4 | As a parent, I see every purchase and get alerts | Push on purchase (optional) and on low balance; statement downloadable | Must | M |
| W2-5 | As a parent, I set an automatic weekly top-up | Saved card through the partner; can be paused | Could | M |
| W2-6 | As a parent, I move money between my children | Recorded as a transfer in the ledger | Should | S |

### W3 Paying at the tuckshop

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| W3-1 | As a cashier, I take a wallet payment by QR, card or wristband | Name and photo shown; limit and blocked items checked; sale in under 2 seconds | Must | L |
| W3-2 | As a cashier, wallet payments keep working offline | Within the learner's cached daily allowance; settled at sync; parent alerted if a sale was rejected later | Must | L |
| W3-3 | As a parent, I block a lost card | Blocked on all school tills at next sync; replacement card links to the same wallet | Must | S |

### W4 Teachers and pre-orders

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| W4-1 | As a teacher, I pay from my own wallet or a monthly staff account | Staff account only if the school enables it; monthly statement to the school | Should | M |
| W4-2 | As a parent, I pre-order lunch for a break | Menu and cut-off time from the tuckshop; paid from the wallet on ordering; cancellable before cut-off | Should | L |
| W4-3 | As a cashier, I see and hand out pre-orders for the break | List by break and class; mark collected | Should | M |

### W5 Money and compliance

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| W5-1 | As the tuckshop owner, I get paid out for wallet sales | Payout schedule from settings; statement of wallet sales and fees | Must | M |
| W5-2 | As a parent, I get unused money back when my child leaves | Refund request; school confirms; partner refunds | Must | M |
| W5-3 | As finance, the ledger reconciles daily with the partner | Any difference creates an alert and blocks payouts until resolved | Must | M |
| W5-4 | As a parent, I can ask for my child's data to be removed | Personal data removed or anonymised; ledger entries kept as required by law | Must | S |

## Data model additions

Wallet tables live in the Wallet service's own database.

| Table | Key columns | Notes |
| --- | --- | --- |
| schools | id, organisation_id, shop_id, emis_number, payout_schedule | Links the school to its tuckshop shop |
| wallet_holders | id, school_id, type (learner, teacher), first_name, last_name, grade, class, photo_url, status | Minimal personal data |
| holder_tokens | holder_id, type (nfc, qr_secret), token_hash, status, issued_at | Lost cards blocked here |
| guardianships | parent_person_id, holder_id, consent_at, consent_version | POPIA consent recorded |
| wallets | id, holder_id, daily_limit_cents, status | Balance is calculated from the ledger |
| wallet_restrictions | wallet_id, product_id or category_code | Blocked items |
| ledger_accounts | id, type (wallet, partner_float, shop_payable, fees), owner_id | Double-entry accounts |
| ledger_entries | id, transaction_id, account_id, amount_cents (+/−), created_at | Every transaction sums to zero |
| ledger_transactions | id, type (top_up, purchase, refund, transfer, payout), reference, partner_ref | Append only |
| daily_allowances | wallet_id, day, allowance_cents, signature | Cached on tills for offline spending |
| pre_orders | id, holder_id, shop_id, break_code, collect_on, items, status, paid_txn_id | Status: placed, ready, collected, cancelled |
| staff_accounts | holder_id, month, total_cents, settled_at | Teacher monthly accounts |
| refund_requests | id, wallet_id, requested_by, amount_cents, status | School confirms |
| reconciliations | day, partner_total_cents, ledger_total_cents, difference_cents, status | Payouts blocked on mismatch |

## API endpoints

| Area | Method and path | Purpose |
| --- | --- | --- |
| Schools | POST /admin/schools, POST /schools/{id}/admins | Register a school and its admin |
| Holders | POST /schools/{id}/holders/import, PATCH /holders/{id} | Learners and teachers |
| Tokens | POST /holders/{id}/tokens, POST /tokens/{id}/block | Issue and block cards and wristbands |
| Parents | POST /guardianships (link code + consent), GET /me/children | Link children |
| Wallets | GET /wallets/{id}, PUT /wallets/{id}/limits, PUT /wallets/{id}/restrictions | Balance, limits, blocked items |
| Top-ups | POST /wallets/{id}/top-ups, POST /schools/{id}/cash-top-ups | Card or EFT through partner; cash at the office |
| Transfers | POST /wallets/transfers | Between a parent's children |
| Payments | POST /wallet-payments | Online wallet payment from the till (idempotent) |
| Payments | GET /wallets/{id}/qr | Rotating signed QR for a learner's phone |
| Sync | POST /sync/upload (wallet sales) | Offline wallet sales settled against allowances |
| Pre-orders | GET /shops/{id}/menu, POST /pre-orders, POST /pre-orders/{id}/cancel, POST /pre-orders/{id}/collect | Order, cancel, hand out |
| Statements | GET /wallets/{id}/statement?format=pdf | Parent statement |
| Refunds | POST /wallets/{id}/refund-requests, POST /admin/refunds/{id}/approve | Leaving school |
| Payouts | GET /shops/{id}/wallet-payouts | Tuckshop payouts and fees |
| Webhooks | POST /payments/webhooks/{partner} | Top-up and refund confirmations |
| Privacy | POST /holders/{id}/erasure-requests | POPIA removal |

## Sync rules: the wallet at an offline till

The till never stores real balances; it stores a signed allowance per learner for the day, so offline selling can't spend money that isn't there.

| Data | Goes to | Direction | Rule |
| --- | --- | --- | --- |
| Holders (name, grade, photo, status) | That school's tills only | Server → device | Server wins; photos cached, removed when a learner leaves |
| Token hashes and blocked cards | That school's tills | Server → device | Server wins; blocked cards refused immediately after sync |
| Daily allowance per wallet | That school's tills, each morning and at every sync | Server → device | Allowance = lower of balance and daily limit; signed by the server |
| Restrictions (blocked items) | That school's tills | Server → device | Server wins |
| Offline wallet sales | Device → server | Device → server | Deducted from the allowance on the till; settled in the ledger at sync; never above the allowance |
| Pre-orders for today | That school's tills | Server → device | Already paid; till only marks collected |

**Rules**

- Two tills in the same school share one allowance by splitting it when offline for more than an hour (configurable), so a learner can't double-spend across tills.
- A sale rejected at settlement (for example the card was blocked on the server before the sale) is reported to the owner and the parent; the shop is protected up to the signed allowance.

## Non-functional acceptance criteria

All Phase 1 and 2 criteria still apply. Phase 3 adds:

| Area | Must pass |
| --- | --- |
| Queue speed | Wallet payment under 2 seconds at the till; 300 learners served in a 20-minute break across two tills |
| Money correctness | Every ledger transaction sums to zero (database constraint and test); daily reconciliation matches the partner to the cent |
| Offline safety | Simulated outage of one school day: no learner spends above their allowance; all sales settle correctly |
| Security | Wallet service passes an independent penetration test before go-live; admin access needs MFA and is logged; QR codes expire within 60 seconds |
| Children's privacy | Parental consent recorded before any data is used; photos visible only on that school's tills; erasure requests completed within 30 days |
| Parent app | Works on budget phones; WCAG 2.2 AA; all six languages |

## Sprint plan

About 16 weeks, two-week sprints.

1. **Sprint 1 – Wallet service and ledger:** service split, separate database and Key Vault, double-entry ledger with tests, partner sandbox.
2. **Sprint 2 – Schools:** W1-1 to W1-3, holder import, card linking.
3. **Sprint 3 – Parent app basics:** W2-1, W2-2, consent flow, wallet app shell.
4. **Sprint 4 – Paying at the till:** W3-1, QR rotation, NFC through Capacitor and USB readers, photo check.
5. **Sprint 5 – Offline wallet:** W3-2, W3-3, signed allowances, settlement at sync, outage simulation.
6. **Sprint 6 – Parent controls:** W2-3, W2-4, W2-6, notifications.
7. **Sprint 7 – Teachers and pre-orders:** W4-1 to W4-3.
8. **Sprint 8 – Money and hardening:** W5-1 to W5-4, reconciliation, payouts, penetration test, privacy review; W2-5 if time allows.

Roll-out: one school for three weeks during a normal school term, then the other pilot schools.

## Ways of working changes

As in Phases 1 and 2, plus:

- **Four-eyes rule for money code:** any change to the ledger, payments or payouts needs two reviewers, one of them a senior developer.
- **Separate production access:** only two named people can reach the Wallet service's production data, through just-in-time access that is logged.
- **Test data:** never real learners' names or photos outside production; generated test schools only.
- **School term calendar:** releases to schools only outside school hours, and never during exam weeks.
- **Definition of done also requires:** ledger balance tests, privacy checks and offline allowance tests pass for any wallet change.
