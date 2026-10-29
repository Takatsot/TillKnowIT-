import { KV_STORE, runOn } from "./tillDb";

/** Small pieces of data kept on the till. Values are plain JSON. */
export interface KeyValueStore {
  get<T>(key: string): Promise<T | undefined>;
  set(key: string, value: unknown): Promise<void>;
  delete(key: string): Promise<void>;
  /** Wipes everything in the store (a wipe request from the owner). */
  clear(): Promise<void>;
}

export class MemoryKeyValueStore implements KeyValueStore {
  private readonly rows = new Map<string, string>();

  async get<T>(key: string): Promise<T | undefined> {
    const raw = this.rows.get(key);
    return raw === undefined ? undefined : (JSON.parse(raw) as T);
  }

  async set(key: string, value: unknown): Promise<void> {
    this.rows.set(key, JSON.stringify(value));
  }

  async delete(key: string): Promise<void> {
    this.rows.delete(key);
  }

  async clear(): Promise<void> {
    this.rows.clear();
  }
}

export class IndexedDbKeyValueStore implements KeyValueStore {
  async get<T>(key: string): Promise<T | undefined> {
    try {
      return (await runOn(KV_STORE, "readonly", (s) => s.get(key) as IDBRequest<T | undefined>)) as T | undefined;
    } catch {
      return undefined;
    }
  }

  async set(key: string, value: unknown): Promise<void> {
    await runOn(KV_STORE, "readwrite", (s) => s.put(value, key));
  }

  async delete(key: string): Promise<void> {
    await runOn(KV_STORE, "readwrite", (s) => s.delete(key));
  }

  async clear(): Promise<void> {
    await runOn(KV_STORE, "readwrite", (s) => s.clear());
  }
}
