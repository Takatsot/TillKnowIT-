import type { CatalogueItem, PromotionDef, PromotionType } from "@tillknowit/domain";
import type { KeyValueStore } from "./localData";
import { loadDeviceConfig } from "./syncRunner";

/** A product as the pull sends it (PulledProduct in ShopCore.Contracts). Deactivated products are included, so the till can stop selling them. */
export interface PulledProduct {
  id: string;
  name: string;
  barcode: string | null;
  categoryCode: string;
  costCents: number;
  priceCents: number;
  lowStockLevel: number;
  expiryDate: string | null;
  isActive: boolean;
  soldByWeight: boolean;
  isAddOn: boolean;
  requiresSerial: boolean;
  parentProductId: string | null;
  variantLabel: string | null;
  updatedAt: string;
}

export interface ProductPull {
  products: PulledProduct[];
  asOf: string;
}

/** What the till keeps of a product: what it sells with (CatalogueItem) and what the stock screens need. */
export interface StoredProduct extends CatalogueItem {
  categoryCode: string;
  lowStockLevel: number;
  expiryDate: string | null;
  isAddOn: boolean;
  requiresSerial: boolean;
  parentProductId: string | null;
  variantLabel: string | null;
}

const PRODUCTS_KEY = "products";
const AS_OF_KEY = "products.asOf";
const PROMOTIONS_KEY = "promotions";

export function mapProduct(p: PulledProduct): StoredProduct {
  return {
    id: p.id, name: p.name, barcode: p.barcode && p.barcode.trim() !== "" ? p.barcode.trim() : null, priceCents: p.priceCents, costCents: p.costCents, soldByWeight: p.soldByWeight,
    categoryCode: p.categoryCode, lowStockLevel: p.lowStockLevel, expiryDate: p.expiryDate, isAddOn: p.isAddOn, requiresSerial: p.requiresSerial,
    parentProductId: p.parentProductId, variantLabel: p.variantLabel,
  };
}

/** Adds and changes products, takes deactivated ones out of the list the till sells from, and remembers when this download was as of. */
export async function applyProductPull(kv: KeyValueStore, pull: ProductPull): Promise<void> {
  const current = (await kv.get<Record<string, StoredProduct>>(PRODUCTS_KEY)) ?? {};
  for (const p of pull.products) {
    if (p.isActive) current[p.id] = mapProduct(p);
    else delete current[p.id];
  }
  await kv.set(PRODUCTS_KEY, current);
  await kv.set(AS_OF_KEY, pull.asOf);
}

/** The `since` of the next download: the moment the last one was as of. Absent on the first. */
export const productsAsOf = (kv: KeyValueStore): Promise<string | undefined> => kv.get<string>(AS_OF_KEY);

/**
 * The products this till can sell: the downloaded ones, by name. Development builds get a few examples only while the till is not registered, so a real shop
 * never sells example products by accident.
 */
export async function loadCatalogue(kv?: KeyValueStore): Promise<StoredProduct[]> {
  const saved = kv ? await kv.get<Record<string, StoredProduct>>(PRODUCTS_KEY) : undefined;
  if (saved) return Object.values(saved).sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
  if (loadDeviceConfig() !== null || !import.meta.env.DEV) return [];
  const base = { categoryCode: "general", lowStockLevel: 0, expiryDate: null, isAddOn: false, requiresSerial: false, parentProductId: null, variantLabel: null };
  return [
    { ...base, id: "dev-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false },
    { ...base, id: "dev-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false },
    { ...base, id: "dev-cooldrink", name: "Cooldrink 330ml", barcode: "6001000000035", priceCents: 1500, costCents: 1000, soldByWeight: false },
    { ...base, id: "dev-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true },
  ];
}

// ---------------------------------------------------------------- promotions

/** A promotion as the server sends it (PromotionResponse in SellingContracts.cs). */
export interface ServerPromotion {
  id: string;
  name: string;
  type: string;
  productIds: string[] | null;
  percent: number;
  amountCents: number;
  items: Array<{ productId: string; quantity: number }> | null;
  startsAt: string;
  endsAt: string;
  days: string[] | null;
  from: string | null;
  to: string | null;
  isActive: boolean;
}

const dayNumbers: Record<string, number> = { sun: 0, mon: 1, tue: 2, wed: 3, thu: 4, fri: 5, sat: 6 };
const promotionTypes: readonly string[] = ["percent_off", "amount_off", "fixed_price", "combo"];

/** "Mon" or "monday" (any case) is 1. Days the till does not know are ignored; no days at all means every day, as on the server. */
export function mapDays(days: readonly string[] | null | undefined): number[] {
  const numbers = (days ?? []).map((d) => dayNumbers[d.trim().slice(0, 3).toLowerCase()]).filter((n): n is number => n !== undefined);
  return numbers.length === 0 ? [0, 1, 2, 3, 4, 5, 6] : [...new Set(numbers)].sort();
}

/** "16:00" or "16:00:00" is "16:00". */
const hhmm = (text: string | null): string | undefined => (text && /^\d{1,2}:\d{2}/.test(text) ? text.slice(0, text.indexOf(":") + 3) : undefined);

/** Promotions of a kind the till does not know are left out: it is better to miss a discount than to price one wrongly. */
export function mapPromotions(rows: readonly ServerPromotion[]): PromotionDef[] {
  return rows
    .filter((p) => promotionTypes.includes(p.type))
    .map((p) => {
      const from = hhmm(p.from);
      const to = hhmm(p.to);
      return {
        id: p.id, name: p.name, type: p.type as PromotionType, productIds: p.productIds ?? [], percent: p.percent, amountCents: p.amountCents,
        items: (p.items ?? []).map((i) => ({ productId: i.productId, quantity: i.quantity })), startsAt: new Date(p.startsAt), endsAt: new Date(p.endsAt),
        days: mapDays(p.days), ...(from && to ? { from, to } : {}), isActive: p.isActive,
      };
    });
}

export async function savePromotions(kv: KeyValueStore, rows: readonly ServerPromotion[]): Promise<void> {
  await kv.set(PROMOTIONS_KEY, rows);
}

export async function loadPromotions(kv?: KeyValueStore): Promise<PromotionDef[]> {
  const saved = kv ? await kv.get<ServerPromotion[]>(PROMOTIONS_KEY) : undefined;
  return saved ? mapPromotions(saved) : [];
}
