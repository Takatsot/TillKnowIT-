import type { Cents } from "./money";
import { fail, ok, type Result } from "./result";

export const paymentMethods = ["cash", "card", "qr", "credit"] as const;
export type PaymentMethod = (typeof paymentMethods)[number];

/** A till may be this far ahead of the server clock; the server rejects anything further. */
export const MAX_CLOCK_SKEW_MS = 24 * 60 * 60 * 1000;

export interface SaleLine {
  productId: string;
  name: string;
  quantity: number;
  priceCents: Cents;
  costCents: Cents;
}

export interface SaleDraft {
  paymentMethod: string;
  vatCents: Cents;
  tenderedCents: Cents | null;
  accountId: string | null;
  soldAt: Date;
  lines: SaleLine[];
}

export interface CheckedSale extends SaleDraft {
  totalCents: Cents;
  changeCents: Cents | null;
}

export function totalOf(lines: readonly SaleLine[]): Cents {
  return lines.reduce((sum, l) => sum + l.priceCents * l.quantity, 0);
}

/**
 * The same checks the server runs on upload (Sale.Record in C#), with the same error codes.
 * A sale that passes here will not be rejected for these reasons when it syncs.
 * The total and change are worked out here, never typed in.
 */
export function checkSale(draft: SaleDraft, now: Date): Result<CheckedSale> {
  const { lines } = draft;
  if (lines.length === 0) return fail("sale.no_lines", "A sale needs at least one item.");
  for (const line of lines) {
    if (line.name.trim() === "") return fail("sale.line_name", "Every item needs a name.");
    if (!Number.isInteger(line.quantity) || line.quantity <= 0)
      return fail("sale.line_quantity", "Item quantity must be more than zero.");
    if (line.priceCents < 0 || line.costCents < 0) return fail("sale.line_price", "Prices cannot be negative.");
  }
  if (new Set(lines.map((l) => l.productId)).size !== lines.length)
    return fail("sale.duplicate_line", "Each product may appear once; add the quantities together.");
  if (!(paymentMethods as readonly string[]).includes(draft.paymentMethod))
    return fail("sale.payment_method", "Unknown payment method.");
  if (draft.soldAt.getTime() > now.getTime() + MAX_CLOCK_SKEW_MS)
    return fail("sale.date_invalid", "The sale is dated in the future.");

  const totalCents = totalOf(lines);
  if (draft.vatCents < 0 || draft.vatCents > totalCents) return fail("sale.vat_invalid", "VAT is not valid for this total.");

  let changeCents: Cents | null = null;
  if (draft.paymentMethod === "cash") {
    if (draft.tenderedCents === null || draft.tenderedCents < totalCents)
      return fail("sale.tender_invalid", "Cash tendered and change do not add up.");
    changeCents = draft.tenderedCents - totalCents;
  } else if (draft.tenderedCents !== null) {
    return fail("sale.tender_invalid", "Only cash sales have tendered and change.");
  }

  if (draft.paymentMethod === "credit" && draft.accountId === null)
    return fail("sale.account_required", "A credit sale needs a credit account.");
  if (draft.paymentMethod !== "credit" && draft.accountId !== null)
    return fail("sale.account_unexpected", "Only credit sales have a credit account.");

  return ok({ ...draft, totalCents, changeCents });
}

/** Quick cash buttons: the exact amount, then the single notes that cover it (R10 to R200). */
export function quickCashOptions(totalCents: Cents): Cents[] {
  const notes = [1000, 2000, 5000, 10000, 20000];
  let covering = notes.filter((n) => n >= totalCents);
  // Above R200 no single note covers it, so round up to the next R100 and R200.
  if (covering.length === 0) covering = [Math.ceil(totalCents / 10000) * 10000, Math.ceil(totalCents / 20000) * 20000];
  return [...new Set([totalCents, ...covering])].sort((a, b) => a - b).slice(0, 4);
}
