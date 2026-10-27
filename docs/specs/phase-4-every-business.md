# TillKnowIT – Phase 4 Development Spec: Every Business

26 October 2026

## Scope of Phase 4

Phase 4 opens TillKnowIT to any business: cafés, butcheries, salons, clothing and hardware shops get the features they need through business-type packs, plus the counter hardware and customer features owners expect from modern POS systems. It builds on Phases 1, 2 and 3.

**Start when**

- The spaza product is stable (low support tickets, offline sync proven).
- At least one pilot business of each new type has agreed to test.

**In Phase 4**

- Business-type packs: café/restaurant/takeaway, butchery/deli/fresh produce, salon/barber/services, clothing and hardware
- Item variants (size, colour, flavour) and modifiers/add-ons
- Discounts and promotions beyond Phase 1 basics: timed promotions, combos, price overrides
- Refunds and returns linked to the original sale
- Open tickets (parked sales) and tables
- Kitchen printer or kitchen screen, order numbers, tips
- Selling by weight with a connected scale and price-embedded barcodes
- Appointments and staff commission for services
- Quotes, invoices, lay-by and gift vouchers
- Customer list and points-based loyalty
- Thermal slip printers and cash drawers
- WhatsApp and SMS slip bundles (paid plan)

**Not in Phase 4**

- Branches, accounting export, pay-later terms, paid supplier tiers, market insights (Phase 5).

## Architecture decisions for Phase 4

New business types are added as configuration and plug-in modules, never as special cases in the selling code.

| Decision | Choice | Why |
| --- | --- | --- |
| Business packs | A pack is data: which feature flags, lookups, screens and receipt template it switches on; stored in `business_packs` and assigned per shop | New business types without code changes in the core |
| Selling extensions | Strategy pattern: `IPricingRule` (weight, variants, modifiers, promotions), `ISaleExtension` (table, ticket, appointment) registered per pack | Open/closed: the sale flow is untouched when a pack is added |
| Hardware | Hardware Abstraction module in the front end: `IPrinter`, `ICashDrawer`, `IScale`, `IKitchenDisplay`, with ESC/POS over WebUSB and Web Bluetooth where supported, and Capacitor plugins on Android | One interface, many devices; unsupported devices fail clearly |
| Kitchen screen | A kitchen view of the till app on a tablet, updated through local network sync when online and queue on the device when not | No extra hardware needed beyond a tablet |
| Customers and loyalty | New Customers module in ShopCore; points as append-only entries like the credit book | Consistent, auditable balances |
| Appointments | New Bookings module with its own schema | Different domain from selling; can be split later |
| Messaging bundles | Usage metered per shop in Messaging; bundle sizes and prices in settings | Pricing changes without a release |

## Repository and module changes

```
backend/src/Modules/
  ShopCore/                          # adds: variants, modifiers, promotions, refunds, tickets, tables, quotes, lay-by, vouchers
  Customers/                         # new: customers, loyalty points
  Bookings/                          # new: services, appointments, staff commission
  Platform/                          # adds: business_packs and pack assignment
frontend/
  packages/hardware/                 # new: IPrinter, ICashDrawer, IScale, IKitchenDisplay + adapters
  packages/packs/                    # new: pack-specific screens loaded by feature flag
    cafe/ butchery/ salon/ retail-variants/
  apps/till/                         # kitchen view, tables, appointments calendar
```

## Epics and user stories

### B1 Business packs

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B1-1 | As a platform admin, I define a business pack | Feature flags, lookups, screens and slip template per pack; versioned | Must | M |
| B1-2 | As an owner, I choose my business type at setup and can change it | Pack switched on; data kept when switching | Must | S |

### B2 Selling for every business

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B2-1 | As an owner, I set up variants (size, colour, flavour) | Each variant has its own barcode, price and stock | Must | M |
| B2-2 | As an owner, I set up modifiers and add-ons with prices | Required or optional groups; minimum and maximum choices | Must | M |
| B2-3 | As an owner, I create timed promotions and combos | Start and end; days and hours; applied automatically at the till | Should | M |
| B2-4 | As a cashier, I refund or exchange items from a past sale | Linked to the original sale; stock and payment reversed; manager PIN above a set amount | Must | M |
| B2-5 | As a cashier, I split a payment across methods | Any mix of cash, card, QR, wallet | Should | S |
| B2-6 | As a cashier, I park a sale and come back to it | Open tickets named or numbered; visible on all tills in the shop after sync | Should | M |

### B3 Café, restaurant and takeaway

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B3-1 | As a waiter, I open a table and add orders | Table map from settings; move or merge tables | Should | M |
| B3-2 | As a cook, I see new orders on the kitchen screen or printer | Order number; mark ready; ready shown to the cashier | Should | M |
| B3-3 | As a cashier, I add a tip | Tip by amount or percentage; tips report per staff | Could | S |

### B4 Butchery, deli and fresh produce

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B4-1 | As a cashier, I sell by weight from a connected scale | Price per kg; weight read from the scale or typed; tare supported | Must | M |
| B4-2 | As a cashier, I scan price-embedded barcodes from a labelling scale | Barcode format rules from settings; weight or price decoded | Should | M |

### B5 Salon, barber and services

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B5-1 | As an owner, I set up services with a duration and price | Linked to staff who can do them | Must | S |
| B5-2 | As staff, I book appointments in a calendar | Day and week view; reminders to the customer by push, WhatsApp or SMS (from the bundle) | Must | L |
| B5-3 | As an owner, I see staff commission | Rules per service or staff from settings; report download | Should | M |

### B6 Clothing and hardware

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B6-1 | As a cashier, I create a quote and turn it into a sale or invoice | PDF quote with expiry; one tap to convert | Should | M |
| B6-2 | As a cashier, I put items on lay-by with a deposit | Payments over time; stock reserved; cancel rules from settings | Should | M |
| B6-3 | As a cashier, I record serial numbers when selling | Required for chosen products; searchable later | Could | S |

### B7 Customers and loyalty

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B7-1 | As a cashier, I attach a customer to a sale | Search by phone; create in seconds; consent for messages | Should | S |
| B7-2 | As an owner, I run a points loyalty programme | Earn and redeem rules from settings; points balance from entries | Could | M |
| B7-3 | As an owner, I sell and redeem gift vouchers | Unique code; balance tracked; expiry from settings | Could | M |

### B8 Hardware and slip bundles

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| B8-1 | As an owner, I connect a 58 or 80 mm thermal printer | USB or Bluetooth; test print; slip layout from template | Must | M |
| B8-2 | As a cashier, the cash drawer opens on cash sales | Through the printer; opening without a sale needs a PIN and is logged | Should | S |
| B8-3 | As an owner, I buy a WhatsApp and SMS slip bundle | Usage shown; warning at 80%; falls back to QR when used up | Should | M |

## Data model additions

| Schema | Table | Key columns | Notes |
| --- | --- | --- | --- |
| platform | business_packs, pack_features | code, version; pack_code, feature_flag, settings_json | Packs as data |
| shopcore | product_variants | id, product_id, attributes_json, barcode, price_cents | Stock movements reference the variant |
| shopcore | modifier_groups, modifiers, product_modifier_groups | min, max, required; name, price_cents | Applied per product |
| shopcore | promotions | id, shop_id, type, rules_json, starts_at, ends_at, schedule | Evaluated by pricing rules |
| shopcore | refunds, refund_lines | sale_id, reason, method, amount_cents; sale_line_id, qty | Linked to the original sale |
| shopcore | sale_payments | sale_id, method_code, amount_cents | Split payments (replaces single method for new sales) |
| shopcore | open_tickets, tables | name, table_id, status; area, seats | Parked sales and floor plan |
| shopcore | kitchen_orders | sale_id, order_no, status, ready_at | Kitchen screen |
| shopcore | quotes, layby_plans, layby_payments | expiry, status; deposit, schedule | Converted to sales |
| shopcore | gift_vouchers, voucher_entries | code, expires_on; amount_cents | Balance from entries |
| shopcore | serial_numbers | sale_line_id, serial | Searchable |
| customers | customers, loyalty_entries | name, phone, consent; points (+/−), sale_id | Points from entries |
| bookings | services, staff_services, appointments, commission_rules | duration, price; staff, starts_at, status; rule_json | Calendar |
| messaging | bundles, usage | shop_id, channel, size, used | Metered slips and reminders |

## API endpoints

Most Phase 4 selling happens on the device and syncs through `POST /sync/upload`; these endpoints cover setup and online features.

| Area | Method and path | Purpose |
| --- | --- | --- |
| Packs | GET /business-packs, PUT /shops/{id}/business-pack, admin CRUD under /admin/business-packs | Choose and manage packs |
| Variants and modifiers | POST /products/{id}/variants, POST /modifier-groups, PUT /products/{id}/modifier-groups | Set up products |
| Promotions | POST /promotions, PATCH /promotions/{id} | Timed promotions and combos |
| Refunds | POST /sales/{id}/refunds | Online refund (offline refunds sync as events) |
| Tables | PUT /shops/{id}/floor-plan | Tables and areas |
| Quotes and lay-by | POST /quotes, POST /quotes/{id}/convert, POST /laybys, POST /laybys/{id}/payments | Quotes and lay-by |
| Vouchers | POST /vouchers, GET /vouchers/{code} | Sell and check |
| Customers | GET /customers?phone=, POST /customers, GET /customers/{id}/points | Customers and loyalty |
| Bookings | GET /appointments?from=&to=, POST /appointments, PATCH /appointments/{id} | Calendar |
| Bundles | POST /shops/{id}/bundles, GET /shops/{id}/bundles/usage | Buy and track slip bundles |
| Reports | GET /reports/{code} adds: tips, commission, refunds, promotions, layby | Same format options as Phase 1 |

## Sync rules

| Data | Goes to | Direction | Rule |
| --- | --- | --- | --- |
| Packs, variants, modifiers, promotions, floor plan | Devices of that shop | Server → device (owner edits sync up) | Last change wins, audited |
| Refunds, split payments, lay-by payments, voucher and loyalty entries | Devices of that shop | Both ways | Append only; balances recalculated after sync |
| Open tickets and tables | Devices of that shop | Both ways | One device "holds" a ticket while editing it; others see it locked; lock expires after 10 minutes offline |
| Kitchen orders | Kitchen screen in the same shop | Device → device through the server; queued offline | Order numbers per device prefix so they never clash offline |
| Appointments | Devices of that shop | Both ways | Double booking checked on the server; conflicts flagged to staff |
| Customers | Devices of that shop | Both ways | Matched by phone number; duplicates merged by the owner |

## Non-functional acceptance criteria

All earlier criteria still apply. Phase 4 adds:

| Area | Must pass |
| --- | --- |
| Core stays clean | Architecture tests prove no pack-specific code inside the core sale flow; adding a test pack needs no core change |
| Hardware | Tested on at least two common printer models, one scale and one cash drawer per connection type; clear message when a device is unsupported |
| Kitchen | New order appears on the kitchen screen within 3 seconds on the same Wi-Fi |
| Variants | A shop with 5,000 variants still adds an item in under 200 ms |
| Spaza unaffected | Spaza shops see no new screens unless their pack switches them on; their install size grows by under 1 MB |

## Sprint plan

About 18 weeks, two-week sprints. Packs can be reordered by demand from pilot businesses.

1. **Sprint 1 – Packs and pricing engine:** B1-1, B1-2, pricing rule strategy, pack loading on the till.
2. **Sprint 2 – Variants and modifiers:** B2-1, B2-2.
3. **Sprint 3 – Refunds, split payments, open tickets:** B2-4, B2-5, B2-6.
4. **Sprint 4 – Hardware:** B8-1, B8-2, hardware abstraction, test lab.
5. **Sprint 5 – Café pack:** B3-1 to B3-3, kitchen screen.
6. **Sprint 6 – Butchery pack:** B4-1, B4-2, scale adapters; B2-3 promotions.
7. **Sprint 7 – Salon pack:** B5-1 to B5-3.
8. **Sprint 8 – Retail extras and customers:** B6-1 to B6-3, B7-1.
9. **Sprint 9 – Loyalty, vouchers, bundles and hardening:** B7-2, B7-3, B8-3, performance and regression tests across all packs.

Roll-out: one pilot business per pack for two weeks each, then open sign-up for that pack.

## Ways of working changes

As in earlier phases, plus:

- **Pack owners:** each pack has a named developer and a pilot business as its "customer" for feedback.
- **Hardware lab:** the supported printers, scales, cash drawers and NFC readers are kept in the office; every release is tested on them.
- **Supported hardware list:** published for owners, so they buy devices that work.
- **Definition of done also requires:** the change is tested with its pack switched on and off, and the spaza pack is regression-tested.
