import type { SaleUploadDto } from "./basket";

/**
 * The sales waiting to reach the server. A sale is written here the moment it is rung up, before anything is sent, so a dead connection or a closed app never
 * loses one. Sending is safe to repeat: the sale id is made on the till and the server stores each id once.
 */
export type OutboxState = "pending" | "rejected";

export interface OutboxEntry {
  id: string;
  sale: SaleUploadDto;
  state: OutboxState;
  attempts: number;
  /** The server's reason, when it refused the sale. Shown to staff; the sale is kept, not lost. */
  rejection?: { code: string; message: string };
  queuedAt: string;
}

export interface Outbox {
  add(entry: OutboxEntry): Promise<void>;
  all(): Promise<OutboxEntry[]>;
  update(entry: OutboxEntry): Promise<void>;
  remove(id: string): Promise<void>;
}

export class MemoryOutbox implements Outbox {
  private readonly rows = new Map<string, OutboxEntry>();

  async add(entry: OutboxEntry): Promise<void> {
    if (!this.rows.has(entry.id)) this.rows.set(entry.id, entry);   // queuing the same sale twice is one sale
  }

  async all(): Promise<OutboxEntry[]> {
    return [...this.rows.values()].sort((a, b) => a.queuedAt.localeCompare(b.queuedAt) || a.id.localeCompare(b.id));
  }

  async update(entry: OutboxEntry): Promise<void> {
    this.rows.set(entry.id, entry);
  }

  async remove(id: string): Promise<void> {
    this.rows.delete(id);
  }
}

export const queueSale = (outbox: Outbox, sale: SaleUploadDto, now: Date): Promise<void> =>
  outbox.add({ id: sale.id, sale, state: "pending", attempts: 0, queuedAt: now.toISOString() });

/** The server's answer to one record, as in UploadRecordResult in C#. */
export interface UploadRecordResult {
  kind: string;
  id: string;
  status: "accepted" | "duplicate" | "rejected";
  code?: string | null;
  message?: string | null;
}

export interface UploadRequest {
  shopId: string;
  deviceId: string;
  sales: SaleUploadDto[];
}

export type SendBatch = (request: UploadRequest) => Promise<{ results: UploadRecordResult[] }>;

export const MAX_BATCH = 500;

export interface FlushOutcome {
  sent: number;
  rejected: number;
  /** True when the connection or the server failed: what was pending stays pending and the caller waits before trying again. */
  failed: boolean;
}

/**
 * Sends the pending sales, oldest first, up to 500 at a time. Accepted and duplicate sales leave the queue (a duplicate was already stored). A rejected sale stays,
 * marked with the server's reason, and is not sent again until staff fix and requeue it. If the request itself fails, nothing changes except the attempt count.
 */
export async function flushOutbox(outbox: Outbox, target: { shopId: string; deviceId: string }, send: SendBatch): Promise<FlushOutcome> {
  const pending = (await outbox.all()).filter((e) => e.state === "pending");
  let sent = 0;
  let rejected = 0;

  for (let start = 0; start < pending.length; start += MAX_BATCH) {
    const batch = pending.slice(start, start + MAX_BATCH);
    let response: { results: UploadRecordResult[] };
    try {
      response = await send({ shopId: target.shopId, deviceId: target.deviceId, sales: batch.map((e) => e.sale) });
    } catch {
      for (const e of batch) await outbox.update({ ...e, attempts: e.attempts + 1 });
      return { sent, rejected, failed: true };
    }

    const byId = new Map(response.results.filter((r) => r.kind === "sale").map((r) => [r.id, r]));
    for (const e of batch) {
      const result = byId.get(e.id);
      if (!result) await outbox.update({ ...e, attempts: e.attempts + 1 });   // no answer for it: try again next time
      else if (result.status === "rejected") {
        rejected++;
        await outbox.update({ ...e, state: "rejected", attempts: e.attempts + 1, rejection: { code: result.code ?? "unknown", message: result.message ?? "The server refused this sale." } });
      } else {
        sent++;
        await outbox.remove(e.id);
      }
    }
  }
  return { sent, rejected, failed: false };
}

/** Seconds to wait before the next try: 5, 10, 20 ... up to five minutes, so a flat battery or a dead line is not hammered. */
export function retryDelaySeconds(failedAttempts: number): number {
  return Math.min(300, 5 * 2 ** Math.max(0, Math.min(failedAttempts - 1, 10)));
}

/** What the till shows next to the online/offline light: how many sales have not reached the server yet, and how many it refused. */
export async function outboxCounts(outbox: Outbox): Promise<{ waiting: number; refused: number }> {
  const all = await outbox.all();
  return { waiting: all.filter((e) => e.state === "pending").length, refused: all.filter((e) => e.state === "rejected").length };
}

/** After staff have fixed what the server complained about, the sale goes back in the queue. */
export async function requeue(outbox: Outbox, id: string, fixed?: SaleUploadDto): Promise<boolean> {
  const entry = (await outbox.all()).find((e) => e.id === id);
  if (!entry || entry.state !== "rejected") return false;
  await outbox.update({ ...entry, sale: fixed ?? entry.sale, state: "pending", rejection: undefined });
  return true;
}
