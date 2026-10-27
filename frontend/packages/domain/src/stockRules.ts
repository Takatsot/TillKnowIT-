import { businessDayOf } from "./cashup";
import { isExpired } from "./product";
import { needsCount, type StockMovementType } from "./stock";

/**
 * The shared stock rules behind the stock list and insights. These match StockRules in C# (ShopCore.Domain),
 * rule for rule, so the till shows the same lists offline that the server reports online.
 */
export const NOT_SELLING_DAYS = 30;
export const EXPIRING_SOON_DAYS = 14;
export const PLANNING_DAYS = 14;

const DAY_MS = 24 * 60 * 60 * 1000;

export interface StockItem {
  productId: string;
  name: string;
  barcode: string | null;
  isActive: boolean;
  addedAt: Date;
  onHand: number;
  lowStockLevel: number;
  /** ISO date (yyyy-mm-dd) or null. */
  expiryDate: string | null;
  /** Net units sold in the last 30 days (sales less cancelled sales). */
  soldLast30Days: number;
  lastSoldAt: Date | null;
}

export type StockFilter = "all" | "low" | "out" | "not_selling" | "expiring";
export type StockFlag = "out" | "low" | "not_selling" | "expired" | "expiring" | "needs_count";

const addDays = (isoDay: string, days: number): string => {
  const [y, m, d] = isoDay.split("-").map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
};

/** Nothing on the shelf, or less than nothing (which also means a count is due). */
export const isOut = (i: StockItem): boolean => i.onHand <= 0;

/** Some left, but at or below the owner's level. A level of 0 means no alert. */
export const isRunningLow = (i: StockItem): boolean => i.onHand > 0 && i.lowStockLevel > 0 && i.onHand <= i.lowStockLevel;

/** Stock sitting on the shelf with no sale for 30 days. A product added in the last 30 days gets the benefit of the doubt. */
export function isNotSelling(i: StockItem, now: Date): boolean {
  const cutoff = now.getTime() - NOT_SELLING_DAYS * DAY_MS;
  return i.onHand > 0 && i.addedAt.getTime() <= cutoff && (i.lastSoldAt === null || i.lastSoldAt.getTime() < cutoff);
}

/** Expires within 14 days, or already has. Only matters while there is stock. */
export const isExpiring = (i: StockItem, today: string): boolean =>
  i.onHand > 0 && i.expiryDate !== null && i.expiryDate <= addDays(today, EXPIRING_SOON_DAYS);

export const dailySalesRate = (i: StockItem): number => i.soldLast30Days / 30;

/** How many days the stock lasts at the recent selling rate; null when nothing sells or nothing is left. */
export function daysLeft(i: StockItem): number | null {
  const rate = dailySalesRate(i);
  return i.onHand > 0 && rate > 0 ? Math.floor(i.onHand / rate) : null;
}

/** What to order to cover the next two weeks, never below zero. */
export const suggestedOrderQuantity = (i: StockItem): number =>
  Math.max(0, Math.ceil(dailySalesRate(i) * PLANNING_DAYS) - Math.max(i.onHand, 0));

export function stockFlags(i: StockItem, now: Date, today: string): StockFlag[] {
  const flags: StockFlag[] = [];
  if (isOut(i)) flags.push("out");
  if (isRunningLow(i)) flags.push("low");
  if (isNotSelling(i, now)) flags.push("not_selling");
  if (isExpired(i.expiryDate, today)) flags.push("expired");
  else if (isExpiring(i, today)) flags.push("expiring");
  if (needsCount(i.onHand)) flags.push("needs_count");
  return flags;
}

export function matchesFilter(i: StockItem, filter: StockFilter, now: Date, today: string): boolean {
  switch (filter) {
    case "low": return isRunningLow(i);
    case "out": return isOut(i);
    case "not_selling": return isNotSelling(i, now);
    case "expiring": return isExpiring(i, today);
    default: return true;
  }
}

/** The stock list: active products only, filtered, in name order. "Today" is the South African business day. */
export function filterStock(items: readonly StockItem[], filter: StockFilter, now: Date): StockItem[] {
  const today = businessDayOf(now);
  return items
    .filter((i) => i.isActive && matchesFilter(i, filter, now, today))
    .sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
}

// ---- counting ----

export const COUNTED_REASON = "counted";

/**
 * Turns a count into the movement that fixes the books, or null when nothing differs.
 * A shortfall with a loss reason (expired, damaged, missing, own use) is a write-off; anything else is a count.
 */
export function countAdjustment(
  onHand: number,
  counted: number,
  reasonCode: string,
): { type: Extract<StockMovementType, "count" | "write_off">; quantity: number } | null {
  const difference = counted - onHand;
  if (difference === 0) return null;
  return difference < 0 && reasonCode !== COUNTED_REASON
    ? { type: "write_off", quantity: difference }
    : { type: "count", quantity: difference };
}

// ---- receiving stock ----

export interface ReceiptLine {
  productId: string;
  name: string;
  quantity: number;
}

/**
 * Scanning in a delivery: each scan adds one. Scans are turned into one line per product, in the order first scanned.
 * Barcodes that match no product are returned separately, so the owner can add them rather than lose them.
 */
export function scansToReceipt(
  scans: readonly string[],
  productsByBarcode: ReadonlyMap<string, { id: string; name: string }>,
): { lines: ReceiptLine[]; unknown: Map<string, number> } {
  const lines = new Map<string, ReceiptLine>();
  const unknown = new Map<string, number>();
  for (const raw of scans) {
    const barcode = raw.trim();
    const product = productsByBarcode.get(barcode);
    if (!product) {
      unknown.set(barcode, (unknown.get(barcode) ?? 0) + 1);
      continue;
    }
    const line = lines.get(product.id);
    if (line) line.quantity += 1;
    else lines.set(product.id, { productId: product.id, name: product.name, quantity: 1 });
  }
  return { lines: [...lines.values()], unknown };
}

export interface ReceiptMovement {
  productId: string;
  type: "receive";
  quantity: number;
  unitCostCents: number;
  supplierName: string | null;
  invoiceRef: string | null;
}

/** One receive movement per product, all sharing the supplier and invoice number. Quantities and costs are checked first. */
export function receiptMovements(
  lines: readonly { productId: string; quantity: number; unitCostCents: number }[],
  supplierName: string | null,
  invoiceRef: string | null,
): { ok: true; movements: ReceiptMovement[] } | { ok: false; code: string; message: string } {
  if (lines.length === 0) return { ok: false, code: "receipt.no_lines", message: "Scan or add at least one item." };
  for (const l of lines) {
    if (!Number.isInteger(l.quantity) || l.quantity <= 0)
      return { ok: false, code: "stock.quantity_sign", message: "A receive movement has the wrong sign." };
    if (l.unitCostCents < 0) return { ok: false, code: "stock.cost_invalid", message: "Cost cannot be negative." };
  }
  const supplier = supplierName?.trim() || null;
  const invoice = invoiceRef?.trim() || null;
  if ((supplier?.length ?? 0) > 200 || (invoice?.length ?? 0) > 100)
    return { ok: false, code: "stock.text_too_long", message: "The supplier or invoice number is too long." };

  return {
    ok: true,
    movements: lines.map((l) => ({
      productId: l.productId, type: "receive", quantity: l.quantity, unitCostCents: l.unitCostCents,
      supplierName: supplier, invoiceRef: invoice,
    })),
  };
}
