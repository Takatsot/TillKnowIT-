import type { Outbox, OutboxEntry } from "@tillknowit/domain";

const DB_NAME = "tillknowit";
const STORE = "outbox";

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) request.result.createObjectStore(STORE, { keyPath: "id" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("The till's storage could not be opened."));
  });
}

async function run<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await open();
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction(STORE, mode);
      const request = work(tx.objectStore(STORE));
      // Resolve only when the transaction has committed, so "saved" really means saved to disk.
      tx.oncomplete = () => resolve(request.result);
      tx.onerror = (event) => reject((event.target as IDBRequest | null)?.error ?? tx.error ?? new Error("The sale could not be saved."));
      tx.onabort = () => reject(tx.error ?? new Error("The sale could not be saved."));
    });
  } finally {
    db.close();
  }
}

/**
 * The sales queue kept in the browser's own database, so it survives a closed tab, a dead battery and a restart. A sale is only called saved once the
 * write has committed. Adding an id that is already there changes nothing.
 */
export class IndexedDbOutbox implements Outbox {
  async add(entry: OutboxEntry): Promise<void> {
    await run("readwrite", (s) => s.add(entry)).catch(async (error: unknown) => {
      // A constraint error means this sale is already queued: that is fine, it is the same sale.
      if ((error as { name?: string } | null)?.name === "ConstraintError") return;
      throw error;
    });
  }

  async all(): Promise<OutboxEntry[]> {
    const rows = await run("readonly", (s) => s.getAll() as IDBRequest<OutboxEntry[]>);
    return rows.sort((a, b) => a.queuedAt.localeCompare(b.queuedAt) || a.id.localeCompare(b.id));
  }

  async update(entry: OutboxEntry): Promise<void> {
    await run("readwrite", (s) => s.put(entry));
  }

  async remove(id: string): Promise<void> {
    await run("readwrite", (s) => s.delete(id));
  }
}
