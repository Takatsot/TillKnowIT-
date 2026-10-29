import type { SaleUploadDto } from "./basket";

/**
 * The sales waiting to reach the server. A sale is written here the moment it is rung up, before anything is sent, so a dead connection or a closed app never
 * loses one. Sending is safe to repeat: the sale id is made on the till and the server stores each id once.
 */
export type OutboxState = "pending" | "rejected";

/**
 * What an outbox entry carries. A sale is "sale"; everything else the server's upload takes (SyncUploadRequest in C#) has a kind of its own, and the kind is also
 * the `kind` the server answers with. Adding a kind here and in `uploadFieldOf` is all it takes to send a new sort of record.
 */
export const recordKinds = ["stock_movement", "cancellation", "refund", "credit_entry", "credit_account", "cash_up", "product"] as const;
export type RecordKind = (typeof recordKinds)[number];
export type OutboxKind = "sale" | RecordKind;

/** The field of the upload request each record kind goes in. */
export const uploadFieldOf = {
  stock_movement: "stockMovements",
  cancellation: "cancellations",
  refund: "refunds",
  credit_entry: "creditEntries",
  credit_account: "creditAccounts",
  cash_up: "cashUps",
  product: "products",
} as const satisfies Record<RecordKind, string>;

interface OutboxBase {
  id: string;
  state: OutboxState;
  attempts: number;
  /** The server's reason, when it refused the record. Shown to staff; the record is kept, not lost. */
  rejection?: { code: string; message: string };
  queuedAt: string;
}

export interface SaleOutboxEntry extends OutboxBase {
  /** Entries written before other kinds existed have no kind: they are sales. */
  kind?: "sale";
  sale: SaleUploadDto;
}

/** Any other record: the JSON exactly as the server's upload takes it. */
export interface RecordOutboxEntry extends OutboxBase {
  kind: RecordKind;
  record: unknown;
}

export type OutboxEntry = SaleOutboxEntry | RecordOutboxEntry;

export const isSaleEntry = (entry: OutboxEntry): entry is SaleOutboxEntry => entry.kind === undefined || entry.kind === "sale";
export const kindOf = (entry: OutboxEntry): OutboxKind => (isSaleEntry(entry) ? "sale" : entry.kind);

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

/** Queues any other record (a stock movement, a refund, a cash-up ...). Like a sale, it is safe to queue twice: its id is made on the till. */
export const queueRecord = (outbox: Outbox, kind: RecordKind, id: string, record: unknown, now: Date): Promise<void> =>
  outbox.add({ id, kind, record, state: "pending", attempts: 0, queuedAt: now.toISOString() });

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
  stockMovements?: unknown[];
  cancellations?: unknown[];
  refunds?: unknown[];
  creditEntries?: unknown[];
  creditAccounts?: unknown[];
  cashUps?: unknown[];
  products?: unknown[];
}

/** One batch of what is pending. Sales are always listed; the other lists appear only when there is something in them. */
export function buildUploadRequest(target: { shopId: string; deviceId: string }, entries: readonly OutboxEntry[]): UploadRequest {
  const request: UploadRequest = { shopId: target.shopId, deviceId: target.deviceId, sales: [] };
  for (const e of entries) {
    if (isSaleEntry(e)) request.sales.push(e.sale);
    else (request[uploadFieldOf[e.kind]] ??= []).push(e.record);
  }
  return request;
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
      response = await send(buildUploadRequest(target, batch));
    } catch {
      for (const e of batch) await outbox.update({ ...e, attempts: e.attempts + 1 });
      return { sent, rejected, failed: true };
    }

    const answers = new Map(response.results.map((r) => [`${r.kind}:${r.id}`, r]));
    for (const e of batch) {
      const result = answers.get(`${kindOf(e)}:${e.id}`);
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
export async function requeue(outbox: Outbox, id: string, fixed?: unknown): Promise<boolean> {
  const entry = (await outbox.all()).find((e) => e.id === id);
  if (!entry || entry.state !== "rejected") return false;
  const changed: OutboxEntry = fixed === undefined ? entry : isSaleEntry(entry) ? { ...entry, sale: fixed as SaleUploadDto } : { ...entry, record: fixed };
  await outbox.update({ ...changed, state: "pending", rejection: undefined });
  return true;
}
