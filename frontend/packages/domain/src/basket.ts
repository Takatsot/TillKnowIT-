import type { Cents } from "./money";
import { vatIncluded } from "./money";
import { fail, ok, type Result } from "./result";
import {
  isRunning, lineGross, parseEmbeddedBarcode, priceBasket, weighedLineTotal,
  type EmbeddedBarcodeRules, type PricedBasket, type PromotionDef, type UnitDivisor,
} from "./selling";

/** What the till knows about a product it can sell. It comes from the local copy of the shop's products. */
export interface CatalogueItem {
  id: string;
  name: string;
  barcode: string | null;
  priceCents: Cents;
  costCents: Cents;
  soldByWeight: boolean;
  /** The shop wants a serial number for each unit sold. Products downloaded from the server carry it; the till then offers to take the serials after the sale. */
  requiresSerial?: boolean;
}

/** One line of the sale in progress. Quantity is units, or grams when the product is sold by weight (then the unit price is per kilogram). */
export interface BasketEntry {
  productId: string;
  name: string;
  quantity: number;
  unitPriceCents: Cents;
  costCents: Cents;
  unitDivisor: UnitDivisor;
  /** Set when a scale label carried the price: the line is exactly that, and no promotion changes it. */
  fixedTotalCents?: Cents;
  /**
   * A manager's price override: the unit price the customer pays instead of unitPriceCents, always lower. It goes on the sale as a discount of the difference
   * with priceOverrideBy as the reason. Both are set together (see setPriceOverride).
   */
  overrideUnitPriceCents?: Cents;
  /** The staff id of the manager or owner who approved the override. */
  overrideApprovedBy?: string;
}

export interface Basket {
  entries: readonly BasketEntry[];
  tipCents: Cents;
  customerId: string | null;
}

export const emptyBasket: Basket = { entries: [], tipCents: 0, customerId: null };

export const MAX_UNITS_PER_LINE = 9_999;

function withEntry(basket: Basket, entry: BasketEntry): Basket {
  const at = basket.entries.findIndex((e) => e.productId === entry.productId && e.fixedTotalCents === undefined && entry.fixedTotalCents === undefined);
  if (at < 0) return { ...basket, entries: [...basket.entries, entry] };
  const existing = basket.entries[at]!;
  const quantity = existing.quantity + entry.quantity;
  return { ...basket, entries: basket.entries.map((e, i) => (i === at ? { ...existing, quantity } : e)) };
}

/** An ordinary item, one more of it or more. Items sold by weight are added with addWeighed instead. */
export function addItem(basket: Basket, item: CatalogueItem, quantity = 1): Result<Basket> {
  if (item.soldByWeight) return fail("basket.needs_weight", `${item.name} is sold by weight. Put it on the scale.`);
  if (!Number.isInteger(quantity) || quantity < 1) return fail("basket.quantity_invalid", "Quantity must be a whole number of one or more.");
  const existing = basket.entries.find((e) => e.productId === item.id);
  if (existing?.fixedTotalCents !== undefined) return fail("basket.label_mixed", `${item.name} is already in the sale at a labelled price. Remove it to add it another way.`);
  const total = (existing?.quantity ?? 0) + quantity;
  if (total > MAX_UNITS_PER_LINE) return fail("basket.quantity_invalid", `At most ${MAX_UNITS_PER_LINE} of one item.`);
  return ok(withEntry(basket, { productId: item.id, name: item.name, quantity, unitPriceCents: item.priceCents, costCents: item.costCents, unitDivisor: 1 }));
}

/** A weighed item: the weight on the scale less the tare. The quantity is kept in grams. */
export function addWeighed(basket: Basket, item: CatalogueItem, grossGrams: number, tareGrams = 0): Result<Basket> {
  if (!item.soldByWeight) return fail("basket.not_by_weight", `${item.name} is not sold by weight.`);
  if (basket.entries.some((e) => e.productId === item.id && e.fixedTotalCents !== undefined)) return fail("basket.label_mixed", `${item.name} is already in the sale at a labelled price. Remove it to add it another way.`);
  const priced = weighedLineTotal(item.priceCents, grossGrams, tareGrams);
  if (!priced.ok) return priced;
  return ok(withEntry(basket, { productId: item.id, name: item.name, quantity: grossGrams - tareGrams, unitPriceCents: item.priceCents, costCents: item.costCents, unitDivisor: 1000 }));
}

export type ScanOutcome = { kind: "item"; basket: Basket; item: CatalogueItem } | { kind: "weigh"; item: CatalogueItem };

/**
 * A barcode from the scanner. A scale label (when the shop uses them) gives the item and the weight or price directly; an ordinary barcode adds one of the item,
 * or asks for the scale if the item is sold by weight. An unknown code is an error the cashier sees, never a silent miss.
 */
export function scan(basket: Basket, code: string, catalogue: readonly CatalogueItem[], rules: EmbeddedBarcodeRules | null): Result<ScanOutcome> {
  const trimmed = code.trim();
  if (rules) {
    const label = parseEmbeddedBarcode(trimmed, rules);
    if (label) {
      const item = catalogue.find((c) => c.barcode === label.itemCode);
      if (!item) return fail("basket.unknown_item", "That label is for an item we do not have.");
      if (label.weightGrams !== null) {
        const added = addWeighed(basket, item, label.weightGrams);
        return added.ok ? ok({ kind: "item", basket: added.value, item }) : added;
      }
      // A price label: the line is exactly that price. A second label of the same item adds its price to the same line, because a product appears once.
      const existing = basket.entries.find((e) => e.productId === item.id);
      if (existing && existing.fixedTotalCents === undefined) return fail("basket.label_mixed", `${item.name} is already in the sale. Remove it to add it by label.`);
      const price = label.priceCents! + (existing?.fixedTotalCents ?? 0);
      const entry: BasketEntry = { productId: item.id, name: item.name, quantity: 1, unitPriceCents: price, costCents: item.costCents + (existing?.costCents ?? 0), unitDivisor: 1, fixedTotalCents: price };
      const entries = existing ? basket.entries.map((e) => (e.productId === item.id ? entry : e)) : [...basket.entries, entry];
      return ok({ kind: "item", basket: { ...basket, entries }, item });
    }
  }
  const item = catalogue.find((c) => c.barcode === trimmed);
  if (!item) return fail("basket.unknown_item", "We do not have that barcode. Add the item or search for it.");
  if (item.soldByWeight) return ok({ kind: "weigh", item });
  const added = addItem(basket, item);
  return added.ok ? ok({ kind: "item", basket: added.value, item }) : added;
}

export function setQuantity(basket: Basket, productId: string, quantity: number): Result<Basket> {
  const entry = basket.entries.find((e) => e.productId === productId);
  if (!entry) return fail("basket.line_not_found", "That item is not in the sale.");
  if (quantity === 0) return ok(removeItem(basket, productId));
  if (!Number.isInteger(quantity) || quantity < 0 || quantity > MAX_UNITS_PER_LINE) return fail("basket.quantity_invalid", "Quantity must be a whole number.");
  if (entry.fixedTotalCents !== undefined) return fail("basket.label_line", "A labelled item cannot be changed. Remove it and scan again.");
  return ok({ ...basket, entries: basket.entries.map((e) => (e.productId === productId ? { ...e, quantity } : e)) });
}

/**
 * A manager approved a lower price for a line. Only a decrease is allowed (a discount), never below zero, and never on a labelled line (its price is the
 * label). The caller has already checked the manager's PIN; the server checks their role again when the sale arrives.
 */
export function setPriceOverride(basket: Basket, productId: string, newUnitPriceCents: Cents, approvedBy: string): Result<Basket> {
  const entry = basket.entries.find((e) => e.productId === productId);
  if (!entry) return fail("basket.line_not_found", "That item is not in the sale.");
  if (entry.fixedTotalCents !== undefined) return fail("basket.label_line", "A labelled item cannot be changed. Remove it and scan again.");
  if (approvedBy.trim() === "") return fail("basket.override_approver", "A price change needs a manager's approval.");
  if (!Number.isInteger(newUnitPriceCents) || newUnitPriceCents < 0) return fail("basket.override_invalid", "The new price must be a whole number of cents, not negative.");
  if (newUnitPriceCents >= entry.unitPriceCents) return fail("basket.override_not_lower", "The price can only be lowered, never raised.");
  return ok({ ...basket, entries: basket.entries.map((e) => (e.productId === productId ? { ...e, overrideUnitPriceCents: newUnitPriceCents, overrideApprovedBy: approvedBy } : e)) });
}

/** Takes a price override off a line, back to the list price. */
export function clearPriceOverride(basket: Basket, productId: string): Basket {
  return { ...basket, entries: basket.entries.map((e) => { if (e.productId !== productId) return e; const { overrideUnitPriceCents: _o, overrideApprovedBy: _b, ...rest } = e; return rest; }) };
}

export function removeItem(basket: Basket, productId: string): Basket {
  return { ...basket, entries: basket.entries.filter((e) => e.productId !== productId) };
}

export function setTip(basket: Basket, tipCents: Cents): Result<Basket> {
  if (!Number.isInteger(tipCents) || tipCents < 0) return fail("basket.tip_invalid", "A tip cannot be negative.");
  return ok({ ...basket, tipCents });
}

export interface PricedLine extends BasketEntry {
  discountCents: Cents;
  /** The promotion behind the discount: the server refuses a discount it cannot explain. */
  promotionId: string | null;
  /** The manager behind a price override discount, or null. A line has a promotion or an override, not both. */
  priceOverrideBy: string | null;
  totalCents: Cents;
}

export interface BasketSummary {
  lines: PricedLine[];
  subtotalCents: Cents;
  discountCents: Cents;
  /** What the items cost after promotions, before any tip. */
  totalCents: Cents;
  tipCents: Cents;
  /** What the customer pays: the total plus the tip. */
  dueCents: Cents;
  /** The VAT inside the total (not the tip), at the shop's rate. */
  vatCents: Cents;
  applied: PricedBasket["applied"];
}

/** The basket as the customer sees it, with promotions worked out the way the server does. Weighed and labelled lines are never discounted. */
export function summariseBasket(basket: Basket, promotions: readonly PromotionDef[], now: Date, vatRatePercent = 15): BasketSummary {
  // A line with a manager's price override is not promoted as well: the override is the discount.
  const promotable = basket.entries.filter((e) => e.unitDivisor === 1 && e.fixedTotalCents === undefined && e.overrideUnitPriceCents === undefined);
  const priced = priceBasket(promotable.map((e) => ({ productId: e.productId, quantity: e.quantity, unitPriceCents: e.unitPriceCents })), promotions.filter((p) => isRunning(p, now)), now);

  const lines: PricedLine[] = basket.entries.map((e) => {
    const gross = e.fixedTotalCents ?? lineGross(e.unitPriceCents, e.quantity, e.unitDivisor);
    if (e.overrideUnitPriceCents !== undefined && e.overrideApprovedBy !== undefined && e.fixedTotalCents === undefined && e.overrideUnitPriceCents < e.unitPriceCents) {
      const overridden = Math.min(gross, gross - lineGross(e.overrideUnitPriceCents, e.quantity, e.unitDivisor));
      return { ...e, discountCents: overridden, promotionId: null, priceOverrideBy: overridden > 0 ? e.overrideApprovedBy : null, totalCents: gross - overridden };
    }
    const discount = Math.min(gross, priced.discountsByProduct.get(e.productId) ?? 0);
    return { ...e, discountCents: discount, promotionId: discount > 0 ? priced.promotionByProduct.get(e.productId) ?? null : null, priceOverrideBy: null, totalCents: gross - discount };
  });
  const subtotal = lines.reduce((sum, l) => sum + l.totalCents + l.discountCents, 0);
  const discount = lines.reduce((sum, l) => sum + l.discountCents, 0);
  const total = subtotal - discount;
  return { lines, subtotalCents: subtotal, discountCents: discount, totalCents: total, tipCents: basket.tipCents, dueCents: total + basket.tipCents, vatCents: vatIncluded(total, vatRatePercent), applied: priced.applied };
}

/** The JSON the server's upload takes for a sale (SaleUpload in C#). */
export interface SaleUploadDto {
  id: string;
  staffId: string;
  soldAt: string;
  paymentMethodCode: string;
  totalCents: Cents;
  vatCents: Cents;
  tenderedCents: Cents | null;
  changeCents: Cents | null;
  accountId: string | null;
  lines: Array<{ productId: string; name: string; quantity: number; priceCents: Cents; costCents: Cents; unitDivisor: number; discountCents: Cents; promotionId: string | null; priceOverrideBy?: string | null }>;
  payments: Array<{ methodCode: string; amountCents: Cents; reference: string | null; tenderedCents: Cents | null }> | null;
  tipCents: Cents;
  customerId: string | null;
}

export const MAX_PAYMENT_PARTS = 6;

export interface Payment {
  /** Wallet, voucher and points can only be a part of a split sale, with the reference of the wallet purchase, the voucher redemption or the points redemption. */
  method: "cash" | "card" | "qr" | "credit" | "wallet" | "voucher" | "points";
  amountCents: Cents;
  /** Cash handed over; the rest is change. */
  tenderedCents?: Cents;
  reference?: string;
  accountId?: string;
}

/**
 * Makes the sale record to keep on the till and upload. The id is made here, on the till, so uploading it twice is one sale. One payment is a plain sale;
 * more than one is a split. What was paid must equal what is due, and cash is the only part that can be more than its share (the extra is change).
 */
export function toSaleUpload(basket: Basket, summary: BasketSummary, payments: readonly Payment[], ids: { saleId: string; staffId: string }, soldAt: Date): Result<SaleUploadDto> {
  if (basket.entries.length === 0) return fail("sale.no_lines", "A sale needs at least one item.");
  if (payments.length === 0) return fail("sale.payment_missing", "Choose how the customer is paying.");
  if (summary.totalCents < 0) return fail("sale.total_invalid", "The total is not valid.");
  if (summary.tipCents > summary.totalCents) return fail("sale.tip_invalid", "A tip cannot be negative or more than the bill.");
  if (payments.length > MAX_PAYMENT_PARTS) return fail("payment.too_many_parts", `A sale can be split into at most ${MAX_PAYMENT_PARTS} payments.`);
  if (new Set(payments.map((p) => p.method)).size !== payments.length) return fail("payment.method_repeated", "Each payment method can be used once.");

  let remaining = summary.dueCents;
  const parts: NonNullable<SaleUploadDto["payments"]> = [];
  let change = 0;
  for (const p of payments) {
    if (!Number.isInteger(p.amountCents) || p.amountCents <= 0) return fail("payment.amount_invalid", "Every payment must be more than zero.");
    if (p.method === "credit" && !p.accountId) return fail("sale.account_required", "A credit sale needs a credit account.");
    if ((p.method === "wallet" || p.method === "voucher" || p.method === "points") && !p.reference) return fail("payment.reference_required", `A ${p.method} payment needs its reference.`);
    if (p.method === "cash") {
      const tendered = p.tenderedCents ?? p.amountCents;
      if (tendered < p.amountCents) return fail("sale.tender_invalid", "Cash tendered and change do not add up.");
      change += tendered - p.amountCents;
      parts.push({ methodCode: "cash", amountCents: p.amountCents, reference: null, tenderedCents: tendered });
    } else {
      if (p.tenderedCents !== undefined) return fail("sale.tender_invalid", "Only cash has tendered and change.");
      parts.push({ methodCode: p.method, amountCents: p.amountCents, reference: p.reference ?? null, tenderedCents: null });
    }
    remaining -= p.amountCents;
  }
  if (remaining !== 0) return fail("sale.payment_mismatch", remaining > 0 ? "The payments do not cover the total yet." : "The payments are more than the total.");

  // One payment by cash, card, QR or credit is a plain sale. Anything else (several payments, or one by wallet or voucher) is a split sale.
  const single = payments.length === 1 && ["cash", "card", "qr", "credit"].includes(payments[0]!.method) ? payments[0]! : null;
  if (!single && payments.some((p) => p.method === "credit")) return fail("sale.split_credit", "Credit cannot be part of a split payment.");

  const lines = summary.lines.map((l) => ({
    productId: l.productId, name: l.name, quantity: l.quantity, priceCents: l.fixedTotalCents ?? l.unitPriceCents, costCents: l.costCents, unitDivisor: l.unitDivisor,
    discountCents: l.discountCents, promotionId: l.promotionId,
    // Only when a manager approved the price, so every other line goes up exactly as before.
    ...(l.priceOverrideBy ? { priceOverrideBy: l.priceOverrideBy } : {}),
  }));
  return ok({
    id: ids.saleId, staffId: ids.staffId, soldAt: soldAt.toISOString(),
    paymentMethodCode: single ? single.method : "split",
    totalCents: summary.totalCents, vatCents: summary.vatCents,
    tenderedCents: single?.method === "cash" ? (single.tenderedCents ?? single.amountCents) : null,
    changeCents: single?.method === "cash" ? change : null,
    accountId: single?.method === "credit" ? single.accountId ?? null : null,
    lines, payments: single ? null : parts, tipCents: summary.tipCents, customerId: basket.customerId,
  });
}
