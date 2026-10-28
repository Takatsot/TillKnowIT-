import type { Cents } from "./money";
import { fail, ok, type Result } from "./result";

/**
 * The selling rules of the till, mirrored from the server (ShopCore.Domain.Selling in C#) so a sale priced offline matches what the server works out on upload.
 * Whole numbers only: cents, grams, units. The server uses integer division on non-negative numbers, which is Math.floor here.
 */

// ---------------------------------------------------------------- line maths

/** 1 for ordinary items, 1000 when the quantity is grams and the price is per kilogram. */
export type UnitDivisor = 1 | 1000;

export const isValidDivisor = (divisor: number): divisor is UnitDivisor => divisor === 1 || divisor === 1000;

/** SaleMath.LineGross: price times quantity, rounded half up to the cent when the quantity is in grams. */
export function lineGross(priceCents: Cents, quantity: number, divisor: UnitDivisor = 1): Cents {
  return divisor <= 1 ? priceCents * quantity : Math.floor((priceCents * quantity + divisor / 2) / divisor);
}

/** SaleMath.LineTotal: the line after its own discount. */
export function lineTotal(priceCents: Cents, quantity: number, divisor: UnitDivisor = 1, discountCents: Cents = 0): Cents {
  return lineGross(priceCents, quantity, divisor) - discountCents;
}

// ---------------------------------------------------------------- B4-1 selling by weight

export const MAX_GRAMS = 50_000;

/** WeightPricing.LineTotal: the net weight (after tare) at the price per kilogram, rounded half up to the cent. */
export function weighedLineTotal(pricePerKgCents: Cents, grossGrams: number, tareGrams = 0): Result<Cents> {
  if (pricePerKgCents < 0) return fail("weight.price_invalid", "The price per kilogram cannot be negative.");
  if (!Number.isInteger(grossGrams) || grossGrams <= 0 || grossGrams > MAX_GRAMS) return fail("weight.weight_invalid", "The weight must be more than zero and at most 50 kg.");
  if (!Number.isInteger(tareGrams) || tareGrams < 0 || tareGrams >= grossGrams) return fail("weight.tare_invalid", "The tare must be less than the weight on the scale.");
  return ok(Math.floor((pricePerKgCents * (grossGrams - tareGrams) + 500) / 1000));
}

// ---------------------------------------------------------------- B4-2 price-embedded barcodes

export type EmbeddedValueType = "weight" | "price";

/** Prefix, then the item code, then the value, then the EAN-13 check digit: together exactly 13 digits. */
export interface EmbeddedBarcodeRules {
  prefix: string;
  itemDigits: number;
  valueDigits: number;
  valueType: EmbeddedValueType;
  decimals: number;
}

export interface EmbeddedBarcode {
  itemCode: string;
  weightGrams: number | null;
  priceCents: Cents | null;
}

export function rulesAreValid(r: EmbeddedBarcodeRules): boolean {
  return r.prefix.length >= 1 && r.prefix.length <= 3 && /^\d+$/.test(r.prefix) && r.itemDigits > 0 && r.valueDigits > 0 && r.decimals >= 0 && r.decimals <= 4
    && r.prefix.length + r.itemDigits + r.valueDigits + 1 === 13;
}

/** The pack setting as the server stores it (JSON), or a reason it cannot be used. */
export function parseBarcodeRules(json: string | null | undefined): Result<EmbeddedBarcodeRules> {
  if (!json || json.trim() === "") return fail("barcode.rules_missing", "This shop has no price-embedded barcode rules.");
  try {
    const raw = JSON.parse(json) as Record<string, unknown>;
    const valueType: EmbeddedValueType = String(raw["valueType"] ?? "").toLowerCase() === "price" ? "price" : "weight";
    const rules: EmbeddedBarcodeRules = {
      prefix: String(raw["prefix"] ?? ""),
      itemDigits: Number(raw["itemDigits"]),
      valueDigits: Number(raw["valueDigits"]),
      valueType,
      decimals: raw["decimals"] === undefined ? (valueType === "weight" ? 3 : 2) : Number(raw["decimals"]),
    };
    return Number.isInteger(rules.itemDigits) && Number.isInteger(rules.valueDigits) && Number.isInteger(rules.decimals) && rulesAreValid(rules)
      ? ok(rules)
      : fail("barcode.rules_invalid", "The barcode rules do not fit a 13-digit code.");
  } catch {
    return fail("barcode.rules_invalid", "The barcode rules are not valid.");
  }
}

/** The EAN-13 check digit for the first 12 digits. */
export function checkDigit(first12: string): number {
  let sum = 0;
  for (let i = 0; i < 12; i++) sum += Number(first12[i]) * (i % 2 === 0 ? 1 : 3);
  return (10 - (sum % 10)) % 10;
}

export function hasValidCheckDigit(code: string): boolean {
  return /^\d{13}$/.test(code) && checkDigit(code.slice(0, 12)) === Number(code[12]);
}

/** Null when the code is not one of this shop's scale labels, so an ordinary barcode goes to the normal lookup. */
export function parseEmbeddedBarcode(code: string | null | undefined, rules: EmbeddedBarcodeRules): EmbeddedBarcode | null {
  if (!code || !hasValidCheckDigit(code) || !code.startsWith(rules.prefix)) return null;
  const start = rules.prefix.length;
  const itemCode = code.slice(start, start + rules.itemDigits);
  const value = Number(code.slice(start + rules.itemDigits, start + rules.itemDigits + rules.valueDigits));
  if (value <= 0) return null;

  if (rules.valueType === "weight") {
    const grams = [value * 1000, value * 100, value * 10, value][rules.decimals] ?? Math.floor(value / 10);
    return grams > 0 && grams <= MAX_GRAMS ? { itemCode, weightGrams: grams, priceCents: null } : null;
  }
  const cents = [value * 100, value * 10, value, Math.floor((value + 5) / 10)][rules.decimals] ?? Math.floor((value + 50) / 100);
  return { itemCode, weightGrams: null, priceCents: cents };
}

/** Builds a label code, for tests and for scales that are set up from here. */
export function buildEmbeddedBarcode(rules: EmbeddedBarcodeRules, itemCode: string, value: number): string {
  const body = rules.prefix + itemCode.padStart(rules.itemDigits, "0").slice(-rules.itemDigits) + String(value).padStart(rules.valueDigits, "0").slice(-rules.valueDigits);
  return body + checkDigit(body);
}

// ---------------------------------------------------------------- B2-2 modifiers

export interface ModifierOption {
  id: string;
  name: string;
  priceCents: Cents;
}

export interface ModifierGroup {
  id: string;
  name: string;
  required: boolean;
  min: number;
  max: number;
  options: readonly ModifierOption[];
}

/** ModifierSelection.Price: checks the choices against every group on the product and returns the extra price. */
export function priceModifiers(groups: readonly ModifierGroup[], chosen: readonly string[]): Result<Cents> {
  if (new Set(chosen).size !== chosen.length) return fail("modifier.repeated", "Each option can be chosen once.");
  const known = new Map(groups.flatMap((g) => g.options).map((o) => [o.id, o]));
  if (chosen.some((id) => !known.has(id))) return fail("modifier.unknown", "One of the choices does not belong to this item.");

  let extra = 0;
  for (const g of groups) {
    const picked = g.options.filter((o) => chosen.includes(o.id));
    if (picked.length > g.max) return fail("modifier.too_many", `${g.name}: choose at most ${g.max}.`);
    if (picked.length < g.min || (g.required && picked.length === 0)) return fail("modifier.too_few", `${g.name}: choose at least ${Math.max(1, g.min)}.`);
    extra += picked.reduce((sum, o) => sum + o.priceCents, 0);
  }
  return ok(extra);
}

// ---------------------------------------------------------------- B2-3 promotions

export type PromotionType = "percent_off" | "amount_off" | "fixed_price" | "combo";

export interface ComboItem {
  productId: string;
  quantity: number;
}

/** Days are 0 (Sunday) to 6 (Saturday), like Date.getDay, in South African time. */
export interface PromotionDef {
  id: string;
  name: string;
  type: PromotionType;
  productIds: readonly string[];
  percent: number;
  amountCents: Cents;
  items: readonly ComboItem[];
  startsAt: Date;
  endsAt: Date;
  days: readonly number[];
  /** "HH:mm", inclusive. */
  from?: string;
  /** "HH:mm", exclusive. */
  to?: string;
  isActive?: boolean;
}

export const SA_OFFSET_MINUTES = 120;

/** The time as South Africans read it: shift by two hours and read the UTC fields. */
function localParts(now: Date): { day: number; minutes: number } {
  const local = new Date(now.getTime() + SA_OFFSET_MINUTES * 60_000);
  return { day: local.getUTCDay(), minutes: local.getUTCHours() * 60 + local.getUTCMinutes() };
}

const toMinutes = (hhmm: string): number => {
  const [h = "0", m = "0"] = hhmm.split(":");
  return Number(h) * 60 + Number(m);
};

export function isRunning(p: PromotionDef, now: Date): boolean {
  if (p.isActive === false || now < p.startsAt || now >= p.endsAt) return false;
  const { day, minutes } = localParts(now);
  if (!p.days.includes(day)) return false;
  if (p.from !== undefined && p.to !== undefined) {
    const from = toMinutes(p.from);
    const to = toMinutes(p.to);
    // A window that runs past midnight (22:00 to 02:00) counts the small hours as part of the evening before.
    return from <= to ? minutes >= from && minutes < to : minutes >= from || minutes < to;
  }
  return true;
}

export interface BasketLine {
  productId: string;
  quantity: number;
  unitPriceCents: Cents;
}

export interface AppliedPromotion {
  promotionId: string;
  name: string;
  discountCents: Cents;
  times: number;
}

export interface PricedBasket {
  subtotalCents: Cents;
  discountCents: Cents;
  totalCents: Cents;
  discountsByProduct: Map<string, Cents>;
  /** The promotion that gave each product most of its discount: a sale line can name only one, and the server needs it to accept a discount. */
  promotionByProduct: Map<string, string>;
  applied: AppliedPromotion[];
}

/** The .NET Guid order for canonical lower-case ids is the order of their text, which is what makes ties break the same way here as on the server. */
const byId = <T extends { id: string }>(a: T, b: T): number => (a.id.toLowerCase() < b.id.toLowerCase() ? -1 : a.id.toLowerCase() > b.id.toLowerCase() ? 1 : 0);

/**
 * PromotionEngine.Price: promotions do not stack. Combos are fitted first, the biggest saving per application, then each product's remaining units
 * get the one per-unit promotion that saves most. Nothing is ever taken off more than a line costs.
 */
export function priceBasket(basket: readonly BasketLine[], promotions: readonly PromotionDef[], now: Date): PricedBasket {
  const subtotal = basket.reduce((sum, l) => sum + l.unitPriceCents * l.quantity, 0);
  const running = promotions.filter((p) => isRunning(p, now));
  if (running.length === 0 || basket.length === 0) return { subtotalCents: subtotal, discountCents: 0, totalCents: subtotal, discountsByProduct: new Map(), promotionByProduct: new Map(), applied: [] };

  const free = new Map<string, number>();
  const price = new Map<string, number>();
  for (const l of basket) {
    free.set(l.productId, (free.get(l.productId) ?? 0) + l.quantity);
    if (!price.has(l.productId)) price.set(l.productId, l.unitPriceCents);
  }
  const discounts = new Map<string, number>();
  const bySource = new Map<string, Map<string, number>>();
  const applied: AppliedPromotion[] = [];
  const add = (product: string, cents: number, promotionId: string) => {
    discounts.set(product, (discounts.get(product) ?? 0) + cents);
    const sources = bySource.get(product) ?? new Map<string, number>();
    sources.set(promotionId, (sources.get(promotionId) ?? 0) + cents);
    bySource.set(product, sources);
  };
  const record = (p: PromotionDef, cents: number, times: number) => {
    const at = applied.findIndex((a) => a.promotionId === p.id);
    if (at >= 0) applied[at] = { ...applied[at]!, discountCents: applied[at]!.discountCents + cents, times: applied[at]!.times + times };
    else applied.push({ promotionId: p.id, name: p.name, discountCents: cents, times });
  };

  // 1. Combos.
  const combos = running.filter((p) => p.type === "combo" && p.items.every((i) => price.has(i.productId))).sort(byId);
  for (;;) {
    let best: PromotionDef | undefined;
    let bestSaving = 0;
    for (const combo of combos) {
      if (combo.items.some((i) => (free.get(i.productId) ?? 0) < i.quantity)) continue;
      const saving = combo.items.reduce((sum, i) => sum + price.get(i.productId)! * i.quantity, 0) - combo.amountCents;
      if (saving > bestSaving) {
        best = combo;
        bestSaving = saving;
      }
    }
    if (!best) break;

    // The saving is shared across the items in proportion to what each would have cost, so the per-line discounts add up to exactly the saving.
    const weights = best.items.map((i) => ({ productId: i.productId, cost: price.get(i.productId)! * i.quantity }));
    const totalCost = weights.reduce((sum, w) => sum + w.cost, 0);
    let given = 0;
    weights.forEach((w, k) => {
      const share = k === weights.length - 1 ? bestSaving - given : Math.floor((bestSaving * w.cost) / totalCost);
      given += share;
      add(w.productId, share, best!.id);
    });
    for (const i of best.items) free.set(i.productId, free.get(i.productId)! - i.quantity);
    record(best, bestSaving, 1);
  }

  // 2. The best per-unit promotion for each product's remaining units, products in id order.
  const perUnitPromotions = running.filter((p) => p.type !== "combo").sort(byId);
  for (const [product, units] of [...free.entries()].filter(([, u]) => u > 0).sort((a, b) => (a[0].toLowerCase() < b[0].toLowerCase() ? -1 : 1))) {
    const unit = price.get(product)!;
    let winner: PromotionDef | undefined;
    let perUnit = 0;
    for (const p of perUnitPromotions) {
      if (p.productIds.length > 0 && !p.productIds.includes(product)) continue;
      const off =
        p.type === "percent_off" ? Math.floor((unit * p.percent + 50) / 100)
        : p.type === "amount_off" ? Math.min(unit, p.amountCents)
        : Math.max(0, unit - p.amountCents);
      if (off > perUnit) {
        winner = p;
        perUnit = off;
      }
    }
    if (!winner) continue;
    add(product, perUnit * units, winner.id);
    record(winner, perUnit * units, units);
  }

  const discount = [...discounts.values()].reduce((sum, v) => sum + v, 0);
  const promotionByProduct = new Map<string, string>();
  for (const [product, sources] of bySource) promotionByProduct.set(product, [...sources.entries()].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))[0]![0]);
  return { subtotalCents: subtotal, discountCents: discount, totalCents: subtotal - discount, discountsByProduct: discounts, promotionByProduct, applied };
}
