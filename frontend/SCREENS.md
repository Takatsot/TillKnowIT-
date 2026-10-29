# How screens are built here

Read this before adding a screen. It is short on purpose.

## The apps

| App | Who it is for | Folder |
| --- | --- | --- |
| Till | Cashiers and managers at the counter, offline first | `apps/till` |
| Back office | Shop owners and managers | `apps/back-office` |
| Supplier portal | Supplier admins and reps | `apps/supplier-portal` |
| Platform admin | Our own staff | `apps/admin` |
| Parent app | Parents of school wallet holders | `apps/parent-app` |
| Insights portal | Brands and suppliers who buy market insights | `apps/insights-portal` |

## Adding a screen area (back office, supplier portal, admin, parent app, insights portal)

One folder per area: `apps/<app>/src/areas/<area>/` with

- `messages.ts`: the area's English text, `export const messages: AreaMessages<Key> = { en }` (see `apps/back-office/src/areas/branches` for the pattern).
  No text is written inline in components. A language is added later as data; until then English shows.
- `<Area>.tsx`: the components.
- `index.ts`: `export const section = defineSection({ id, order, title, messages, needs: [...], component })`.
- `<Area>.test.tsx`: tests with a fake `Api` (see the branches test).

The shell finds every `areas/*/index.ts` by itself. **Do not edit `sections.ts`, `main.tsx` or any shared file** to add an area.
If an app needs more than one screen in an area, use tabs or sub-views inside your own component.

`apps/back-office/src/areas/branches` is the reference. Copy its shape.

## Adding a till feature

One folder per feature: `apps/till/src/features/<feature>/` with `messages.ts`, components, `index.ts` exporting
`export const feature = defineFeature({ id, order, title, messages, flag, roles, requiresOnline, component })`, and tests.
The component gets `{ ctx }: { ctx: TillContext }` (see `apps/till/src/feature.ts`): the staff member, the pack's features, the api, the device, the outbox, the
catalogue, the hardware ports. `flag` is the pack feature that switches the tab on (for example `"tables"`); leave it out for tabs every shop has.
Do not edit `App.tsx` or `SellScreen.tsx` unless your task says you own them.

## Rules that apply everywhere

1. **Server calls** go through `useApi()` (app-kit) in the apps and `ctx.api` in the till. Every call returns a `Result`: `{ ok: true, value }` or
   `{ ok: false, code, message }`. Show `message` to the person; switch on `code` when you need a special case. Never let an error be thrown at a screen.
   Read the real routes and request/response shapes in `backend/src/Modules/*/*.Contracts` and the `*Endpoints.cs` files. Do not guess them.
2. **Who is calling**: until sign-in exists, many routes take a person id (`callerPersonId`, `personId`, `CallerPersonId`). Use `useSession().personId`.
   Mark nothing as secure that is not: the stand-in is documented in `packages/app-kit/src/session.tsx`.
3. **Money** is whole cents everywhere. Show it with `formatRand`, read it with `parseRandToCents` (both in `@tillknowit/domain`, re-exported by app-kit).
   Dates and times show in South African time with `formatDate` / `formatDateTime`.
4. **Business rules are not rewritten in screens.** If a rule exists in `@tillknowit/domain` (pricing, promotions, weight, barcodes, basket, payments), use it.
   If the server owns a rule, show what the server says.
5. **Components** come from `@tillknowit/design-system` (`Button`, `TextField`, `SelectField`, `CheckField`, `Field`, `Card`, `Notice`, `Table`, `Badge`, `Stack`,
   `Inline`). Touch targets are 48px. Colours and sizes come from the tokens, never raw values. If you need a new shared component, add it to the design
   system in a new file and export it from `index.ts` (adding a line there is allowed).
6. **Accessible**: every control has a label, errors use `role="alert"` (the `Notice` kind `danger` does it), a list or table is a real list or table,
   and everything works with the keyboard.
7. **Offline**: the till never waits for the network to sell. Features that need the server say so and are disabled offline (`requiresOnline`).
   Back-office apps may assume a connection but must show `network.unreachable` clearly.
8. **Plans**: some features need a paid plan (`plan.feature_not_included`). Show a friendly explanation, not the raw code.
9. **Tests**: each area has tests with a fake `Api` (`Partial<Api>` cast as `Api`) and `userEvent`, as in the branches test. Cover the main path, a server
   refusal, and the empty state. Run `npx vitest run --root apps/<app>` and `npx tsc -p apps/<app> --noEmit`; both must be clean before you stop.
10. **Do not claim what you did not check.** Do not invent endpoints. If something you need is missing from the server, say so in your report instead of faking it.

## Commands

```
cd frontend
npx vitest run --root apps/<app>      # tests of one app
npx tsc -p apps/<app> --noEmit        # typecheck
npm test                              # everything
```
