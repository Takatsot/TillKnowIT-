# TillKnowIT – Phase 5 Development Spec: Growth

26 October 2026

## Scope of Phase 5

Phase 5 grows TillKnowIT from single shops into a business: owners run several branches, accountants get clean exports, suppliers pay for more reach, shops can buy stock on credit through a finance partner, and the platform earns from anonymised insights. It builds on Phases 1 to 4.

**Start when**

- Enough active shops and suppliers to make insights and paid tiers worth paying for (target to be set from pilot numbers).
- A registered credit provider has agreed to partner on pay-later terms.
- Legal review of market insights and data sharing is complete (POPIA, competition rules).

**In Phase 5**

- Branches: several shops under one owner, stock transfers, combined reports
- Accounting export to Sage and Xero
- Paid supplier tiers and sponsored promotions billing
- Paid shop plan (based on the pilot pricing decision)
- Pay-later terms for shops through a registered finance partner
- Anonymised, opt-in market insights for brands and suppliers
- Group buying started by shops at scale (stokvels, school groups)
- More languages (Sepedi, Xitsonga, Tshivenda, isiNdebele, siSwati)
- Public API for partners (read-only to start)

**Not in Phase 5**

- Expanding beyond South Africa (separate decision).

## Architecture decisions for Phase 5

| Decision | Choice | Why |
| --- | --- | --- |
| Branches | Organisation hierarchy: an owner organisation with child shops; permissions can apply to one shop or all | Reuses the membership model; no new login type |
| Stock transfers | Transfer as a pair of stock movements (out at one branch, in at the other) linked by a transfer ID | Same append-only stock rules |
| Accounting export | Adapter per package (`IAccountingExporter`: Sage, Xero) behind the Messaging and jobs worker; daily journal export with account mapping in settings | Add packages without touching reporting |
| Billing | New Billing module: plans, subscriptions, invoices, usage (messages, sponsored placements); payments through the gateway | One place for every charge to shops and suppliers |
| Pay-later | We don't lend: the finance partner decides and lends; we share sales history only with the owner's explicit consent, through the partner's API | Lending is regulated; we stay a technology provider |
| Insights | Separate Insights service and analytics database (PostgreSQL warehouse or Azure Synapse later), fed by anonymised daily aggregates; minimum group size of 10 shops per figure | Analytics never touches live shop data; individual shops can't be identified |
| Public API | Versioned, read-only partner API through the gateway with API keys, scopes and rate limits | Partners integrate safely |

## Repository and module changes

```
backend/src/
  Host/
    TillKnowIT.Insights.Api/          # new: Insights service (separate database)
    TillKnowIT.PartnerApi/            # new: public read-only API host
  Modules/
    Identity/                         # adds: organisation hierarchy, branch-scoped permissions
    ShopCore/                         # adds: stock transfers, combined reports
    Billing/                          # new: plans, subscriptions, invoices, usage
    Finance/                          # new: pay-later consent and partner adapter
    Insights/                         # new: aggregation jobs, anonymisation rules
    Messaging/                        # adds: IAccountingExporter adapters (Sage, Xero)
frontend/apps/
  back-office/                        # split out from the till: branches, billing, exports
  insights-portal/                    # new: for brands and suppliers who buy insights
packages/i18n/                         # adds: nso, ts, ve, nr, ss
```

## Epics and user stories

### G1 Branches

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G1-1 | As an owner, I add a second shop under my business | Shared products optional; prices can differ per branch | Must | M |
| G1-2 | As an owner, I transfer stock between branches | Out and in movements linked; in-transit shown until received | Must | M |
| G1-3 | As an owner, I see reports for one branch or all together | Branch filter on every report; combined totals | Must | M |
| G1-4 | As an owner, I give a manager access to one branch only | Branch-scoped memberships | Must | S |

### G2 Accounting export

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G2-1 | As an owner, I connect Sage or Xero | OAuth connection; account mapping screen | Should | M |
| G2-2 | As an owner, my daily sales, VAT and stock purchases post automatically | Daily journal; failures retried and shown | Should | L |

### G3 Billing and paid plans

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G3-1 | As a platform admin, I define plans for shops and suppliers | Features, limits and price per plan in settings | Must | M |
| G3-2 | As an owner or supplier, I upgrade, downgrade or cancel | Pro-rata rules from settings; invoice emailed | Must | M |
| G3-3 | As a supplier, I pay for sponsored placements | Budget, areas, dates; billed by use; always labelled | Should | M |
| G3-4 | As finance, failed payments are handled | Reminders; features limited after a grace period; core till never blocked | Must | M |

### G4 Pay-later terms

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G4-1 | As an owner, I apply for stock on credit | Explicit consent to share sales history with the named partner; consent can be withdrawn | Should | M |
| G4-2 | As an owner, I pay a supplier order with approved terms | Partner pays the supplier; repayments tracked in the app | Should | L |

### G5 Market insights

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G5-1 | As an owner, I choose whether my anonymised data is included | Off by default; a clear explanation; can change anytime | Must | S |
| G5-2 | As a brand or supplier, I buy area trend reports | Category and area trends; every figure from at least 10 shops; no shop names | Should | L |

### G6 Scale and reach

| ID | Story | Acceptance criteria | Priority | Size |
| --- | --- | --- | --- | --- |
| G6-1 | As a stokvel or school group, we start group buys at scale | Group admin role; members invited; shared order history | Could | M |
| G6-2 | As a user, I use the app in Sepedi, Xitsonga, Tshivenda, isiNdebele or siSwati | All text translated and reviewed by first-language speakers | Should | M |
| G6-3 | As a partner, I read data I've been granted through the public API | API key, scopes, rate limits; owner approves each partner | Could | L |

## Data model additions

| Schema | Table | Key columns | Notes |
| --- | --- | --- | --- |
| identity | organisations (adds parent_id) | parent_id | Owner organisation → branches |
| identity | memberships (adds scope) | scope (one branch or all) | Branch-scoped access |
| shopcore | stock_transfers | id, from_shop_id, to_shop_id, status, sent_at, received_at | Links two stock movements |
| messaging | accounting_connections, account_mappings, export_runs | provider, tokens (Key Vault ref); category → account; status, errors | Sage and Xero |
| billing | plans, plan_features | code, audience (shop, supplier), price_cents; feature, limit | Plans as data |
| billing | subscriptions, invoices, invoice_lines, usage_records | org, plan, status; amounts; metric, qty | Usage from messaging and sponsored placements |
| finance | credit_consents, credit_applications, credit_repayments | shop, partner, consent_at, withdrawn_at; status; amount, due | Partner decides and lends |
| insights | insight_opt_ins | shop_id, opted_in, changed_at | Off by default |
| insights (separate DB) | daily_area_aggregates | area, category, day, units, revenue_band, shop_count | Only rows with shop_count ≥ 10 are published |
| platform | api_clients, api_scopes, api_grants | client, key hash; scope; shop approval | Public API |

## API endpoints

| Area | Method and path | Purpose |
| --- | --- | --- |
| Branches | POST /organisations/{id}/branches, GET /organisations/{id}/branches | Add and list branches |
| Transfers | POST /stock-transfers, POST /stock-transfers/{id}/receive | Move stock between branches |
| Reports | GET /reports/{code}?branch=all\|{id} | Combined or per-branch |
| Accounting | POST /accounting/connections/{provider}, PUT /accounting/mappings, GET /accounting/export-runs | Connect, map, monitor |
| Billing | GET /plans?audience=, POST /subscriptions, PATCH /subscriptions/{id}, GET /invoices | Plans and invoices |
| Sponsored | POST /sponsored-campaigns, GET /sponsored-campaigns/{id}/usage | Supplier promotions |
| Pay-later | POST /credit/consents, DELETE /credit/consents/{id}, POST /credit/applications | Consent and apply |
| Insights | PUT /shops/{id}/insights-opt-in, GET /insights/reports?area=&category= | Opt in; buy reports |
| Partner API | GET /partner/v1/... (scoped, read-only) | For approved partners |

## Sync rules

| Data | Goes to | Direction | Rule |
| --- | --- | --- | --- |
| Branch data | Only that branch's devices | As in Phase 1 | A till never holds another branch's sales |
| Owner devices | Owner's phone with "all branches" scope | Server → device | Summaries for all branches; detail fetched online |
| Stock transfers | Sending and receiving branches | Both ways | Out movement on send, in movement on receive; in-transit until both sync |
| Billing, plans, insights, accounting | Not synced | Online only | Till never depends on billing being reachable |
| Plan limits | Devices of that shop | Server → device | Cached; a lapsed plan never stops core selling |

## Non-functional acceptance criteria

All earlier criteria still apply. Phase 5 adds:

| Area | Must pass |
| --- | --- |
| Scale | Platform handles 10,000 shops and 2 million sales a day; reports for an owner with 20 branches return in under 10 seconds (or run in the background) |
| Anonymity | Automated test: no published insight figure comes from fewer than 10 shops; no shop or customer identifiers leave the Insights service |
| Consent | Pay-later data sharing only with active consent; withdrawal stops sharing within 1 hour |
| Billing | Invoices match usage records exactly; VAT on our invoices correct; core selling never blocked by billing status |
| Partner API | Rate limits enforced; every call logged with client and scope; keys rotatable without downtime |
| Languages | Eleven languages, each reviewed by a first-language speaker |

## Sprint plan

About 18 weeks, two-week sprints. Epics are independent, so they can be reordered by business priority.

1. **Sprint 1 – Branches:** G1-1, G1-4, organisation hierarchy, branch-scoped permissions.
2. **Sprint 2 – Transfers and combined reports:** G1-2, G1-3; back office split into its own app.
3. **Sprint 3 – Billing:** G3-1, G3-2, G3-4.
4. **Sprint 4 – Supplier tiers and sponsored billing:** G3-3, usage metering.
5. **Sprint 5 – Accounting export:** G2-1, G2-2 (Xero first, then Sage).
6. **Sprint 6 – Pay-later:** G4-1, G4-2 with the finance partner's sandbox.
7. **Sprint 7 – Insights:** G5-1, G5-2, Insights service and anonymisation tests.
8. **Sprint 8 – Languages and groups:** G6-2, G6-1.
9. **Sprint 9 – Partner API and scale hardening:** G6-3, load tests at 10,000 shops, security review.

Roll-out: each epic released behind a feature flag to a small group first.

## Ways of working changes

As in earlier phases, plus:

- **Data ethics review:** any new insight or data-sharing feature is reviewed by the information officer and a legal adviser before release.
- **Finance partner governance:** a written agreement covers what data is shared, for how long, and who is responsible for lending decisions and complaints.
- **Customer success:** paid plans need a support promise (response times) and a way to measure churn.
- **Teams by area:** at this size, split into small teams owning areas (till and shop core, marketplace, wallet, growth), each with its own backlog and on-call rota.
- **Definition of done also requires:** billing, consent and anonymity tests pass for any change in those areas.
