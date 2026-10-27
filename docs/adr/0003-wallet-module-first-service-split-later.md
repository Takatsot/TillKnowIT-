# ADR 0003: The wallet is its own module and can have its own database now; its own service comes at deployment

Status: Accepted, 2026-10-27. This refines the Phase 3 spec's "Wallet and payments as its own Container App with its own database".

## Context

Wallet money is the most sensitive data in the system and the spec wants it isolated: its own database, Key Vault and admin access.
The isolation that matters for the code is already in place: the Wallet module owns its schema and its outbox, reaches other modules only through their contracts
(architecture tests enforce it), keeps its signing keys apart from everything else, and moves money only through one ledger with unique references.

## Decision

- The Wallet module is built and tested like the others and runs in the same hosts.
- `ConnectionStrings:Wallet` points the module at a database of its own; without it, it uses the main database (its own `wallet` schema either way).
- `Wallet:QrKey` and `Wallet:AllowanceKey` are required, at least 32 characters, and different from each other. They belong in the wallet's own Key Vault.
- Splitting it into its own Container App is a deployment change: a host that registers only Identity (for people, shops and permissions), Messaging and Wallet. The code does not change.
  The cross-module calls it makes (`IShopDirectory`, `IPersonDirectory`, `ITillAccessChecker`, `IPermissionChecker`, `IMessageQueue`) are the interfaces that become HTTP or message calls at that point.
- Payments (supplier payments from Phase 2) stays a separate module. The spec moves it into the wallet service; nothing in the code needs that, and mixing supplier money with parents' money in one module would weaken the isolation, not strengthen it.

## Consequences

- Production access to the wallet database can be restricted to two named people now, by giving it its own connection string and credentials.
- Four-eyes review, the independent penetration test and just-in-time production access are process and infrastructure items, tracked in the hand-over list.
- Until a partner is connected the wallet cannot take real money: every call to it fails with `payment.partner_not_connected`.
