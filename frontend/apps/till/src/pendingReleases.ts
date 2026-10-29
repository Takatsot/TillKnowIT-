import type { Api } from "@tillknowit/api-client";
import type { Outbox } from "@tillknowit/domain";
import type { Caller } from "./features/till-kit/kit";
import type { KeyValueStore } from "./localData";

/**
 * Money taken for a sale that was then given up: a voucher redemption, points, or a school wallet purchase. Each is given back once, online.
 * Routes: POST /shops/{shopId}/vouchers/release, POST /shops/{shopId}/customers/{customerId}/points/release, POST /shops/{shopId}/wallet-payments/{purchaseId}/void.
 * When there is no connection (or the call is lost) a note waits in the till's own database and is tried again later, like pendingSerials.ts.
 * Nothing is ever given back once the sale is in the upload queue: the server refuses it too (payment.sale_recorded).
 */

export type Release =
  | { kind: "voucher"; code: string; reference: string; saleId: string }
  | { kind: "points"; customerId: string; reference: string; saleId: string }
  | { kind: "wallet"; purchaseId: string; saleId: string };

const KEY = "releases.pending";

export async function pendingReleases(kv: KeyValueStore): Promise<Release[]> {
  return (await kv.get<Release[]>(KEY)) ?? [];
}

const same = (a: Release, b: Release) => a.kind === b.kind && JSON.stringify(a) === JSON.stringify(b);

async function save(kv: KeyValueStore, list: Release[]): Promise<void> {
  await kv.set(KEY, list);
}

const HELD_KEY = "releases.held";

async function heldNotes(kv: KeyValueStore): Promise<Release[]> {
  return (await kv.get<Release[]>(HELD_KEY)) ?? [];
}

/**
 * Written the moment a part is taken, before the cashier has finished paying. If the till is closed or crashes with the part still on screen, the note is still here
 * the next time the app starts, and adoptHeldReleases turns it into something to give back.
 */
export async function holdRelease(kv: KeyValueStore, r: Release): Promise<void> {
  const list = await heldNotes(kv);
  if (!list.some((o) => same(o, r))) await kv.set(HELD_KEY, [...list, r]);
}

/** The sale went through, or the part was given back (or refused for good): the note is no longer needed. */
export async function dropHeldRelease(kv: KeyValueStore, match: { saleId: string } | Release): Promise<void> {
  const list = await heldNotes(kv);
  const rest = list.filter((o) => ("kind" in match ? !same(o, match) : o.saleId !== match.saleId));
  if (rest.length !== list.length) await kv.set(HELD_KEY, rest);
}

/**
 * Called once when the app starts, before any sale can be under way: whatever is still held belongs to a sale that never finished, so it becomes a note to give back.
 * (A sale that did reach the queue is told apart later: sendPendingReleases skips a note whose sale is waiting to upload, and the server refuses to give back a sale it has.)
 */
export async function adoptHeldReleases(kv: KeyValueStore): Promise<number> {
  const held = await heldNotes(kv);
  if (held.length === 0) return 0;
  for (const r of held) await remember(kv, r);
  await kv.set(HELD_KEY, []);
  return held.length;
}

async function remember(kv: KeyValueStore, r: Release): Promise<void> {
  const list = await pendingReleases(kv);
  if (!list.some((o) => same(o, r))) await save(kv, [...list, r]);
}

async function forget(kv: KeyValueStore, r: Release): Promise<void> {
  await save(kv, (await pendingReleases(kv)).filter((o) => !same(o, r)));
}

export function callRelease(api: Api, c: Caller, r: Release) {
  const who = { deviceId: c.deviceId, staffId: c.staffId };
  switch (r.kind) {
    case "voucher": return api.post<unknown>(`/shops/${c.shopId}/vouchers/release`, { ...who, code: r.code, reference: r.reference });
    case "points": return api.post<unknown>(`/shops/${c.shopId}/customers/${r.customerId}/points/release`, { ...who, reference: r.reference });
    case "wallet": return api.post<unknown>(`/shops/${c.shopId}/wallet-payments/${r.purchaseId}/void`, who);
  }
}

/** A reason that goes away by itself. Anything else is the server saying no, and trying again will not change it. */
const transient = (code: string) => code.startsWith("network.");

export type ReleaseOutcome = "given_back" | "waiting" | "refused";

/**
 * Gives one thing back now when online, otherwise (or when the connection fails) keeps a note to try later.
 * `refused` carries the server's words: the money was not given back and will not be (for example the sale had already reached the server).
 */
export async function releaseNow(deps: { kv: KeyValueStore; api?: Api; caller: Caller | null; online: boolean }, r: Release): Promise<{ outcome: ReleaseOutcome; message?: string }> {
  if (!deps.online || !deps.api || !deps.caller) {
    await remember(deps.kv, r);
    return { outcome: "waiting" };
  }
  const result = await callRelease(deps.api, deps.caller, r);
  if (result.ok) return { outcome: "given_back" };
  if (transient(result.code)) {
    await remember(deps.kv, r);
    return { outcome: "waiting" };
  }
  return { outcome: "refused", message: result.message };
}

export interface SendReleasesResult {
  given: number;
  waiting: number;
  failed: Array<{ message: string }>;
}

let running = false;

/** Tries every waiting note. A note whose sale is in the upload queue is dropped without a call: that money is part of the sale. */
export async function sendPendingReleases(deps: { kv: KeyValueStore; api: Api; caller: Caller; outbox: Outbox }): Promise<SendReleasesResult> {
  const result: SendReleasesResult = { given: 0, waiting: 0, failed: [] };
  if (running) return result;
  running = true;
  try {
    const queued = new Set((await deps.outbox.all()).map((e) => e.id));
    for (const r of await pendingReleases(deps.kv)) {
      if (queued.has(r.saleId)) {
        await forget(deps.kv, r);
        continue;
      }
      const c = await callRelease(deps.api, deps.caller, r);
      if (c.ok) {
        result.given += 1;
        await forget(deps.kv, r);
      } else if (transient(c.code)) result.waiting += 1;
      else {
        result.failed.push({ message: c.message });
        await forget(deps.kv, r);
      }
    }
  } finally {
    running = false;
  }
  return result;
}
