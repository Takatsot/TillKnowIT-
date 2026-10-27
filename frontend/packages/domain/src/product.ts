import type { Cents } from "./money";
import { fail, ok, type Result } from "./result";
import { MAX_CLOCK_SKEW_MS } from "./sale";

export interface ProductDraft {
  name: string;
  /** Empty or null for products with no barcode, such as loose sweets. */
  barcode: string | null;
  categoryCode: string;
  costCents: Cents;
  priceCents: Cents;
  lowStockLevel: number;
  /** ISO date (yyyy-mm-dd) or null. */
  expiryDate: string | null;
  changedAt: Date;
}

export const isBarcode = (value: string): boolean => /^\d{6,14}$/.test(value.trim());

/**
 * The same checks the server runs when a product syncs (Product.Check in C#), with the same error codes.
 * Category and duplicate-barcode checks need data, so the till runs those against its local lists.
 */
export function checkProduct(draft: ProductDraft, now: Date, knownCategories: readonly string[]): Result<ProductDraft> {
  const name = draft.name.trim();
  if (name === "" || name.length > 200) return fail("product.name_invalid", "A product needs a name (up to 200 characters).");

  const barcode = (draft.barcode ?? "").trim();
  if (barcode !== "" && !isBarcode(barcode)) return fail("product.barcode_invalid", "A barcode is 6 to 14 digits.");
  if (draft.costCents < 0 || draft.priceCents < 0) return fail("product.price_invalid", "Cost and price cannot be negative.");
  if (draft.lowStockLevel < 0 || !Number.isInteger(draft.lowStockLevel))
    return fail("product.low_stock_invalid", "The low-stock level cannot be negative.");
  if (draft.changedAt.getTime() > now.getTime() + MAX_CLOCK_SKEW_MS)
    return fail("product.date_invalid", "The change is dated in the future.");
  if (!knownCategories.includes(draft.categoryCode)) return fail("product.category_invalid", "Unknown category.");

  return ok({ ...draft, name, barcode: barcode === "" ? null : barcode });
}

/** True when a barcode is already used by a different product of this shop. Products with no barcode never clash. */
export function barcodeTaken(
  barcode: string | null,
  productId: string,
  products: readonly { id: string; barcode: string | null }[],
): boolean {
  if (!barcode) return false;
  return products.some((p) => p.id !== productId && p.barcode === barcode);
}

/** Last change wins, by the time on the change. The same rule the server applies. */
export function incomingChangeWins(incoming: Date, current: Date): boolean {
  return incoming.getTime() > current.getTime();
}

export const isExpired = (expiryDate: string | null, today: string): boolean => expiryDate !== null && expiryDate < today;

export interface SellableItem {
  name: string;
  /** ISO date (yyyy-mm-dd) or null. */
  expiryDate: string | null;
  /** The manager who approved selling this item although it has expired, with their PIN. */
  expiryOverrideBy?: string | null;
}

/**
 * E3-5: items that stop a sale because they have expired and nobody approved selling them.
 * An item that expires today is still sellable. The server checks that the approving manager's role allows the override.
 */
export const expiredItemsBlockingSale = (items: readonly SellableItem[], today: string): string[] =>
  items.filter((i) => isExpired(i.expiryDate, today) && !i.expiryOverrideBy).map((i) => i.name);
