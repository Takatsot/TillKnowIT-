# TillKnowIT – Phase 2 Development Spec: Supplier Marketplace

Oct 26, 2026 · @Someone

## Scope of Phase 2

Phase 2 turns the founding-supplier listing into a full marketplace: suppliers offer stock when shops run low or stock expires, and shops order and pay the way they choose. It builds on the [Phase 1 Development Spec](https://claude.ai/artifact/AxtbfpBy4AVh2MFFg32bum) and the [product specification](https://claude.ai/artifact/Gn1vUkz4dGfTLAqcHWJE3j).

**Start when**

- Most pilot shops are still selling weekly a month after install.
- A payment partner that can hold money until delivery is signed.
- 5–10 founding suppliers in the pilot areas are ready to list.

**In Phase 2**

- Supplier onboarding and verification for all supplier types
- Supplier portal: catalogue, prices, delivery areas and days, minimum order, orders inbox
- Reorder options on low-stock and expiry alerts, with true cost per unit
- Deals, including labelled sponsored deals
- Purchase orders by app, WhatsApp or email; receive into stock with one tap
- Paying suppliers: on delivery or in the app (held until delivery is confirmed)
- Group buying
- Supplier ratings
- Demand totals for suppliers (opt-in, totals only)
- Sharing settings for shops
- Phase 1 follow-ups the pilot asks for most

**Not in Phase 2**

- Pay-later terms, paid supplier tiers (Phase 5), school wallet (Phase 3).

## Architecture decisions for Phase 2

The Marketplace module grows into the first separately deployed service, because suppliers are a new kind of user with their own traffic and release pace.

| Decision | Choice | Why |
| --- | --- | --- |
| Marketplace deployment | Split out as its own Container App and database schema owner, behind the YARP gateway | First real service; proves the split path designed in Phase 1 |
| Events | Switch from in-process to Azure Service Bus for ShopCore ↔ Marketplace events (StockRunningLow, ProductExpiring, OrderDelivered) | Reliable delivery between separately deployed apps |
| Supplier portal | New front-end app `apps/supplier-portal`, sharing the design system and API client | Different users, online-only, desktop-first |
| Payments | New Payments module (later part of the Wallet and payments service) using the partner's hosted checkout and hold-until-delivery feature | Card data never touches our servers |
| Offer ranking | Rules engine with weights stored in settings (true cost per unit first, then rating, then delivery time); sponsored deals marked, never ranked above cheaper non-sponsored ones by default | Fair and explainable; tunable without code |
| Group buying | Saga in Marketplace: open → threshold reached or deadline → split into individual orders, or cancelled with no charge | Clear states; compensation if a step fails |
| Search | PostgreSQL full-text search on supplier products, with trigram matching for barcodes and names | Enough for pilot scale; no extra search service yet |

## Repository and module changes

Same repository and rules as Phase 1; new and changed parts only:

```
backend/src/
  Host/
    TillKnowIT.Marketplace.Api/        # new: Marketplace deployed on its own
  Modules/
    Marketplace/                       # grows: suppliers, offers, orders, groups, ratings
      Marketplace.Domain/
      Marketplace.Application/
      Marketplace.Infrastructure/      # schema "marketplace", Service Bus publisher and consumer
      Marketplace.Contracts/
    Payments/                          # new module
      Payments.Domain/ Payments.Application/ Payments.Infrastructure/ Payments.Contracts/
  BuildingBlocks/
    TillKnowIT.Messaging.ServiceBus/   # new: Service Bus adapter behind the outbox publisher
frontend/apps/
  supplier-portal/                     # new React app
infra/bicep/
  marketplace.bicep, servicebus.bicep  # new
```

## Epics and user stories

Same priority (MoSCoW) and size (S, M, L) scale as Phase 1.

### M1 Supplier onboarding

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M1-1 | As a supplier, I apply to join | Business details, registration number, contacts, delivery areas; status pending | Must | M |
| M1-2 | As a platform admin, I verify or reject a supplier | Checklist recorded; verified badge; reason on rejection | Must | S |
| M1-3 | As a supplier admin, I invite my sales reps | Reps get the supplier-rep role from the roles table | Should | S |

### M2 Supplier catalogue and deals

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M2-1 | As a supplier, I upload my catalogue | Excel or CSV with column matching; products matched to the shared catalogue by barcode; case size, case price, unit price, VAT flag | Must | L |
| M2-2 | As a supplier, I set delivery areas, days, minimum order and delivery fee | Areas from the municipalities lookup | Must | M |
| M2-3 | As a supplier, I create a deal | Type (discount, free goods, combo), terms, start and end, areas; optional sponsored flag | Must | M |

### M3 Offers to shops

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M3-1 | As an owner, I see reorder options when stock runs low or expires | Usual supplier first with last price; others ranked by true cost per unit incl. delivery and VAT; cheapest marked; margin at my current price shown | Must | L |
| M3-2 | As an owner, I control who sees my low-stock signals | Off, usual suppliers only, or all suppliers in my area | Must | S |
| M3-3 | As an owner, I see a suggested replacement for items not selling | Based on items selling well in similar nearby shops (aggregated only) | Could | M |

### M4 Ordering and delivery

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M4-1 | As an owner, I build orders from my order list | One order per supplier; minimum order warning | Must | M |
| M4-2 | As an owner, I send an order in the app, or by WhatsApp or email to suppliers not on the platform | WhatsApp and email use templates; order status tracked either way | Must | M |
| M4-3 | As a supplier, I confirm, change or decline order lines and set a delivery date | Shop notified of every change | Must | M |
| M4-4 | As an owner, I receive a delivered order into stock with one tap | Opens the Stock in review prefilled; short deliveries adjust the order | Must | M |

### M5 Paying suppliers

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M5-1 | As an owner, I choose per order to pay on delivery or in the app | Both always available; order shows paid or owing | Must | M |
| M5-2 | As an owner, I pay in the app and the money is released when I confirm delivery | Partner hosted checkout; release on confirmation or after 72 hours with no dispute | Must | L |
| M5-3 | As an owner or supplier, I raise a dispute on an order | Dispute holds payment; platform admin resolves | Should | M |
| M5-4 | As a supplier, I see one statement of orders, payments and amounts owing | Download as PDF or Excel | Should | M |

### M6 Group buying

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M6-1 | As a supplier, I post a group deal | Item, bulk price, minimum total quantity, closing date, area | Must | M |
| M6-2 | As an owner, I join a group deal with my quantity | Progress bar of group total; other shops' names and quantities never shown | Must | M |
| M6-3 | As the system, I close the group at the deadline | Reached: split into one order per shop. Not reached: everyone notified, nobody charged | Must | M |
| M6-4 | As an owner, I start a group with shops I invite | Invite by code or phone number | Could | M |

### M7 Ratings and demand

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| M7-1 | As an owner, I rate a delivery | On time, complete, score 1–5; one rating per order | Should | S |
| M7-2 | As a supplier, I see demand totals in my areas | Count of opted-in shops low on items I sell; never names or sales figures | Should | M |

## Data model additions

Same conventions as Phase 1 (UUID v7, money in cents, audit columns).

| Schema | Table | Key columns | Notes |
| --- | --- | --- | --- |
| marketplace | suppliers | id, organisation\_id, registration\_no, verified\_at, min\_order\_cents, delivery\_fee\_cents, payment\_terms | Links to identity.organisations |
| marketplace | supplier\_areas | supplier\_id, municipality\_code, delivery\_days | Lookup-driven |
| marketplace | supplier\_products | id, supplier\_id, catalogue\_product\_id, case\_size, case\_price\_cents, unit\_price\_cents, vat\_included, in\_stock, expiry\_policy | Matched to the shared catalogue |
| marketplace | deals | id, supplier\_id, type\_code, terms\_json, starts\_at, ends\_at, areas, is\_sponsored | Type from lookup |
| marketplace | shop\_suppliers | shop\_id, supplier\_id, usual\_for\_categories, last\_price\_cents, last\_order\_at | The usual supplier |
| marketplace | sharing\_settings | shop\_id, mode (off, usual, all) | Owner choice |
| marketplace | purchase\_orders | id, shop\_id, supplier\_id, status, channel (app, whatsapp, email), delivery\_date, payment\_method (on\_delivery, in\_app), payment\_status | Status: draft, sent, confirmed, delivered, cancelled, disputed |
| marketplace | purchase\_order\_lines | order\_id, supplier\_product\_id, qty, agreed\_price\_cents, delivered\_qty | Short delivery tracked |
| marketplace | group\_deals | id, supplier\_id, supplier\_product\_id, bulk\_price\_cents, min\_total\_qty, closes\_at, status | Status: open, reached, failed |
| marketplace | group\_commitments | group\_deal\_id, shop\_id, qty, order\_id | Order created when reached |
| marketplace | ratings | order\_id, shop\_id, supplier\_id, on\_time, complete, score | One per order |
| payments | payments | id, order\_id, partner\_ref, amount\_cents, status (held, released, refunded, disputed), released\_at | Money held by the partner, not us |
| payments | disputes | id, order\_id, raised\_by, reason, status, resolution | Admin resolves |
| marketplace | demand\_snapshots | supplier\_id, catalogue\_product\_id, area, shops\_low\_count, taken\_at | Aggregates only, from opted-in shops |

## API endpoints

Same conventions as Phase 1 (`/api/v1`, OpenAPI, idempotency keys, problem details).

| Area | Method and path | Purpose |
| --- | --- | --- |
| Suppliers | POST /suppliers/applications, POST /admin/suppliers/{id}/verify | Apply and verify |
| Suppliers | PUT /suppliers/{id}/areas, POST /suppliers/{id}/reps | Delivery areas, invite reps |
| Catalogue | POST /suppliers/{id}/products/import, PATCH /supplier-products/{id} | Upload and edit catalogue |
| Deals | POST /deals, PATCH /deals/{id}, GET /deals?area= | Manage and browse deals |
| Offers | GET /shops/{id}/offers?product= | Ranked reorder options for one product |
| Offers | GET /shops/{id}/order-list | Low and expiring items with best options |
| Sharing | PUT /shops/{id}/sharing | Off, usual, all |
| Orders | POST /purchase-orders, POST /purchase-orders/{id}/send | Create and send (app, WhatsApp, email) |
| Orders | PATCH /purchase-orders/{id}/lines (supplier) | Confirm, change, decline lines |
| Orders | POST /purchase-orders/{id}/deliver, POST /purchase-orders/{id}/receive | Mark delivered; receive into stock |
| Payments | POST /purchase-orders/{id}/payments | Start in-app payment; returns partner checkout link |
| Payments | POST /payments/webhooks/{partner} | Partner callbacks (signed and verified) |
| Payments | POST /purchase-orders/{id}/disputes, POST /admin/disputes/{id}/resolve | Disputes |
| Groups | POST /group-deals, POST /group-deals/{id}/commitments, GET /group-deals?area= | Post, join, browse |
| Ratings | POST /purchase-orders/{id}/rating | Rate a delivery |
| Demand | GET /suppliers/{id}/demand | Aggregated demand in the supplier's areas |
| Statements | GET /suppliers/{id}/statement?format=pdf\|xlsx | Supplier statement |

## Sync rules

The marketplace is mostly online, but the till keeps working offline for the parts owners use at the counter.

| Data | Goes to | Direction | Rule |
| --- | --- | --- | --- |
| Usual suppliers, their prices and the shop's open orders | Devices of that shop | Server → device | Server wins; refreshed at every sync |
| Draft orders made offline | Device → server | Device → server | Sent when online; owner sees "waiting to send" |
| Delivery confirmation and receiving | Device → server | Device → server | Stock movements append as in Phase 1; payment release waits for the confirmation to reach the server |
| Other suppliers' offers, deals, group deals | Not synced | Fetched online | Shown as "Connect to see more options" when offline |
| Supplier portal data | Not on shop devices | — | Supplier portal is online only |

## Non-functional acceptance criteria

All Phase 1 criteria still apply. Phase 2 adds:

| Area | Must pass |
| --- | --- |
| Offer speed | Ranked reorder options shown within 2 seconds on 3G |
| Events | No lost events between ShopCore and Marketplace under a service restart (tested) |
| Payments | Webhooks verified by signature; every payment state change audited; daily reconciliation with the partner matches to the cent |
| Fairness | Automated test: sponsored deals are labelled and never outrank a cheaper non-sponsored option by default |
| Privacy | Automated test: no supplier endpoint returns a shop's name, sales or stock unless that shop chose to share |
| Group buying | Closing a group with 500 commitments completes in under 1 minute; no shop charged when a group fails |
| Supplier portal | Works on desktop and tablet browsers; WCAG 2.2 AA |

## Sprint plan

About 14 weeks with the same team, in two-week sprints.

1. **Sprint 1 – Service split and suppliers:** Marketplace deployed on its own, Service Bus live, M1-1 to M1-3, supplier portal shell.
2. **Sprint 2 – Catalogue and areas:** M2-1, M2-2, catalogue matching, delivery areas.
3. **Sprint 3 – Offers:** M3-1, M3-2, ranking rules in settings, order list on the till.
4. **Sprint 4 – Ordering:** M4-1 to M4-4, WhatsApp and email order templates, receive into stock.
5. **Sprint 5 – Payments:** M5-1 to M5-4, partner integration, webhooks, reconciliation.
6. **Sprint 6 – Deals and group buying:** M2-3, M6-1 to M6-3.
7. **Sprint 7 – Ratings, demand and hardening:** M7-1, M7-2, fairness and privacy tests, load tests, security review; M3-3 and M6-4 if time allows.

Roll-out: founding suppliers first with 5 pilot shops for two weeks, then all pilot shops and new suppliers.

## Ways of working changes

Environments, branching, definition of ready and definition of done stay as in Phase 1. Additions:

- **Partner sandbox:** the payment partner's test environment is wired into Development and Staging; production keys only in Key Vault.
- **Contract tests:** ShopCore and Marketplace check each other's event and API contracts on every build, so a change in one cannot silently break the other.
- **Supplier support:** a supplier onboarding checklist and a named contact for founding suppliers.
- **Definition of done also requires:** fairness and privacy tests pass for any change touching offers, demand or payments.
