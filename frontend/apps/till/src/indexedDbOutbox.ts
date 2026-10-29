import type { Outbox, OutboxEntry } from "@tillknowit/domain";
import { OUTBOX_STORE, runOn } from "./tillDb";

const FAILURE = "The sale could not be saved.";

/**
 * The upload queue kept in the browser's own database, so it survives a closed tab, a dead battery and a restart. A sale (or any other record) is only called
 * saved once the write has committed. Adding an id that is already there changes nothing.
 */
export class IndexedDbOutbox implements Outbox {
  async add(entry: OutboxEntry): Promise<void> {
    await runOn(OUTBOX_STORE, "readwrite", (s) => s.add(entry), FAILURE).catch(async (error: unknown) => {
      // A constraint error means this entry is already queued: that is fine, it is the same one.
      if ((error as { name?: string } | null)?.name === "ConstraintError") return;
      throw error;
    });
  }

  async all(): Promise<OutboxEntry[]> {
    const rows = await runOn(OUTBOX_STORE, "readonly", (s) => s.getAll() as IDBRequest<OutboxEntry[]>, FAILURE);
    return rows.sort((a, b) => a.queuedAt.localeCompare(b.queuedAt) || a.id.localeCompare(b.id));
  }

  async update(entry: OutboxEntry): Promise<void> {
    await runOn(OUTBOX_STORE, "readwrite", (s) => s.put(entry), FAILURE);
  }

  async remove(id: string): Promise<void> {
    await runOn(OUTBOX_STORE, "readwrite", (s) => s.delete(id), FAILURE);
  }
}
