import type { Api } from "@tillknowit/api-client";
import type { KeyValueStore } from "../../localData";
import type { DeviceConfig } from "../../syncRunner";

/** One product in the stock list (StockRow in ShopCore.Contracts). */
export interface StockRow {
  productId: string;
  name: string;
  barcode: string | null;
  onHand: number;
  lowStockLevel: number;
  expiryDate: string | null;
  daysLeft: number | null;
  suggestedOrderQuantity: number;
  flags: string[];
}

export interface StockCopy {
  rows: StockRow[];
  /** When the figures were fetched (ISO). */
  asOf: string;
}

const KEY = "stock.rows";

export async function cachedStock(kv: KeyValueStore | undefined): Promise<StockCopy | null> {
  return (await kv?.get<StockCopy>(KEY)) ?? null;
}

/** The stock list from the server, kept on the till for when it is offline. Null when the server could not be reached. */
export async function fetchStock(api: Api, device: DeviceConfig | null, kv: KeyValueStore | undefined, now: Date): Promise<{ copy: StockCopy | null; error: string | null }> {
  if (!device) return { copy: await cachedStock(kv), error: null };
  const result = await api.get<StockRow[]>(`/shops/${device.shopId}/stock`);
  if (!result.ok) return { copy: await cachedStock(kv), error: result.message };
  const copy = { rows: result.value, asOf: now.toISOString() };
  await kv?.set(KEY, copy);
  return { copy, error: null };
}
