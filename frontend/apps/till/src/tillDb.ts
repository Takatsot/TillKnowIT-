/**
 * The till's own database in the browser. Version 1 had only the sales queue; version 2 adds a small key-value store for what is downloaded (staff, products,
 * promotions) and for the till's own books (recent sales, cash taken in). Both live in one database so one request opens them.
 */
export const DB_NAME = "tillknowit";
export const OUTBOX_STORE = "outbox";
export const KV_STORE = "kv";
const VERSION = 2;

export function openTillDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(OUTBOX_STORE)) db.createObjectStore(OUTBOX_STORE, { keyPath: "id" });
      if (!db.objectStoreNames.contains(KV_STORE)) db.createObjectStore(KV_STORE);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("The till's storage could not be opened."));
  });
}

/** Runs one request in its own transaction and resolves only when the transaction has committed, so "saved" really means saved to disk. */
export async function runOn<T>(storeName: string, mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>, failure = "The data could not be saved."): Promise<T> {
  const db = await openTillDb();
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction(storeName, mode);
      const request = work(tx.objectStore(storeName));
      tx.oncomplete = () => resolve(request.result);
      tx.onerror = (event) => reject((event.target as IDBRequest | null)?.error ?? tx.error ?? new Error(failure));
      tx.onabort = () => reject(tx.error ?? new Error(failure));
    });
  } finally {
    db.close();
  }
}
