import type { Api } from "@tillknowit/api-client";
import type { Outbox } from "@tillknowit/domain";
import { recordSerials, type SerialItem } from "./features/serials/serialApi";
import type { Caller } from "./features/till-kit/kit";
import type { KeyValueStore } from "./localData";

/**
 * Serial numbers typed at the till right after a sale. The server can only take them for a sale it already has (POST /shops/{shopId}/sales/{saleId}/serials answers
 * serial.sale_unknown otherwise), and the sale is still in the upload queue at that moment. So they wait here, in the till's own database, and are sent once the
 * sale has arrived. They survive closing the app.
 */

export interface PendingSerials {
  saleId: string;
  items: SerialItem[];
}

const KEY = "serials.pending";

export async function pendingSerials(kv: KeyValueStore): Promise<PendingSerials[]> {
  return (await kv.get<PendingSerials[]>(KEY)) ?? [];
}

/** Keeps serials for a sale. Typing them again for the same sale replaces what was waiting, so pressing twice is one set. */
export async function queuePendingSerials(kv: KeyValueStore, saleId: string, items: readonly SerialItem[]): Promise<void> {
  const rest = (await pendingSerials(kv)).filter((p) => p.saleId !== saleId);
  await kv.set(KEY, [...rest, { saleId, items: [...items] }]);
}

export interface SendSerialsResult {
  sent: number;
  /** Still waiting for their sale to reach the server, or for a connection. */
  waiting: number;
  /** The server refused these for good (for example a serial already sold). They are dropped from the queue and the cashier is told. */
  failed: Array<{ saleId: string; message: string }>;
}

let running = false;

/** A reason that goes away by itself: no connection, or the sale has not arrived yet. Anything else is the server saying no. */
const transient = (code: string) => code.startsWith("network.") || code === "serial.sale_unknown";

/** Sends every waiting set whose sale has left the upload queue. Safe to call as often as you like. */
export async function sendPendingSerials(deps: { kv: KeyValueStore; api: Api; caller: Caller; outbox: Outbox }): Promise<SendSerialsResult> {
  const result: SendSerialsResult = { sent: 0, waiting: 0, failed: [] };
  if (running) return result;
  running = true;
  try {
    const queued = new Set((await deps.outbox.all()).map((e) => e.id));
    for (const entry of await pendingSerials(deps.kv)) {
      if (queued.has(entry.saleId)) {
        result.waiting += 1;
        continue;
      }
      const r = await recordSerials(deps.api, deps.caller, entry.saleId, entry.items);
      if (r.ok) {
        result.sent += 1;
        await remove(deps.kv, entry.saleId);
      } else if (transient(r.code)) result.waiting += 1;
      else {
        result.failed.push({ saleId: entry.saleId, message: r.message });
        await remove(deps.kv, entry.saleId);
      }
    }
  } finally {
    running = false;
  }
  return result;
}

async function remove(kv: KeyValueStore, saleId: string): Promise<void> {
  await kv.set(KEY, (await pendingSerials(kv)).filter((p) => p.saleId !== saleId));
}
