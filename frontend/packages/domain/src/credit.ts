import type { Cents } from "./money";
import { fail, ok, type Result } from "./result";

export type CreditEntryType = "sale" | "payment" | "cancel";

export interface CreditEntryDraft {
  type: CreditEntryType;
  /** Signed: a credit sale is positive, a payment or a cancelled credit sale is negative. */
  amountCents: Cents;
  saleId?: string | null;
  paymentMethod?: string | null;
}

/** Same checks as CreditEntry.Record on the server, with the same error codes. */
export function checkCreditEntry(entry: CreditEntryDraft): Result<CreditEntryDraft> {
  if (entry.amountCents === 0) return fail("credit.amount_zero", "A credit entry cannot be zero.");

  const signOk = entry.type === "sale" ? entry.amountCents > 0 : entry.amountCents < 0;
  if (!signOk) return fail("credit.amount_sign", `A ${entry.type} entry has the wrong sign.`);

  if (entry.type === "payment") {
    if (entry.saleId) return fail("credit.sale_unexpected", "A payment is not tied to a sale.");
    if (!["cash", "card", "qr"].includes(entry.paymentMethod ?? ""))
      return fail("credit.payment_method", "A payment is made with cash, card or QR.");
  } else if (!entry.saleId) {
    return fail("credit.sale_required", "Sale and cancel entries must point at their sale.");
  }
  return ok(entry);
}

/** What a customer owes: the sum of their entries. Never stored, always recalculated after a sync. */
export const balanceOf = (signedAmounts: readonly Cents[]): Cents => signedAmounts.reduce((sum, a) => sum + a, 0);

/** Over the limit means owing more than it. No limit means never over. */
export const overLimit = (balanceCents: Cents, limitCents: Cents | null): boolean =>
  limitCents !== null && balanceCents > limitCents;

/**
 * Warn the cashier before a credit sale that would push the customer over their limit.
 * It is only a warning: the owner decides, and the sale is never blocked.
 */
export const wouldExceedLimit = (balanceCents: Cents, saleTotalCents: Cents, limitCents: Cents | null): boolean =>
  overLimit(balanceCents + saleTotalCents, limitCents);

/** The entry a credit sale creates, and the one that undoes it when the sale is cancelled. */
export const creditSaleEntry = (saleId: string, totalCents: Cents): CreditEntryDraft => ({ type: "sale", amountCents: totalCents, saleId });
export const creditCancelEntry = (saleId: string, totalCents: Cents): CreditEntryDraft => ({ type: "cancel", amountCents: -totalCents, saleId });
