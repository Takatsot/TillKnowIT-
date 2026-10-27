# ADR 0002: Marketplace and Payments stay in the modular monolith for now

Status: Accepted, 2026-10-27. This changes the Phase 2 spec's "Marketplace deployed on its own, Service Bus live" sprint item.

## Context

The Phase 2 spec splits Marketplace into its own service with Azure Service Bus between it and ShopCore.
What that split protects is the boundary: modules that only talk through contracts and events, each with its own schema and outbox.
That boundary is already enforced here by architecture tests (modules reference only each other's Contracts; no cross-schema tables), and every cross-module event goes through the transactional outbox.
Deploying a second service today would add a message broker, a second pipeline and distributed failure modes before there is any load or team boundary that asks for it.

## Decision

Marketplace, Payments and Messaging run in the same API and Worker hosts as the other modules.
The outbox already delivers events at least once, so handlers are idempotent (tested: the stock-in review from a received order, group closing, payment callbacks).

Split a module out when one of these is true: it needs to scale or deploy on its own, a separate team owns it, or its failures must be isolated from the till's.
When that happens: add a Service Bus implementation behind the outbox dispatcher (publish the stored event; consumers subscribe and call the same `IDomainEventHandler`), give the module its own host, and keep the contracts as they are.

## Consequences

- Nothing in module code changes at the split. Only a host and the event transport do.
- Cross-module writes (for example a payment moving an order's payment status) are two commits, not one. The order status is moved after the money, and the daily reconciliation catches any drift.
- The supplier portal is a separate front-end app and can be built against the same API whenever the screens are designed; it needs no separate backend.
