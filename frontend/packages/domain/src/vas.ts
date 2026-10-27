import type { Cents } from "./money";
import { fail, ok, type Result } from "./result";

export type VasProduct = "airtime" | "data" | "electricity";

/** The same limits as VasRules on the server. They are placeholders until the partner's rate card is signed. */
export const vasLimits: Record<VasProduct, { min: Cents; max: Cents }> = {
  airtime: { min: 500, max: 100_000 },
  data: { min: 500, max: 100_000 },
  electricity: { min: 2_000, max: 200_000 },
};

/** Commission in basis points (100 = 1%), rounded down to the cent. */
export const vasCommission = (amountCents: Cents, basisPoints: number): Cents => Math.floor((amountCents * basisPoints) / 10_000);

function cleanPhone(text: string): string | null {
  let d = text.replace(/[^\d+]/g, "");
  if (d.startsWith("00")) d = "+" + d.slice(2);
  if (d.startsWith("0")) d = "+27" + d.slice(1);
  else if (d.length > 0 && !d.startsWith("+")) d = "+" + d;
  return /^\+\d{9,15}$/.test(d) ? d : null;
}

export interface VasDraft {
  product: VasProduct;
  recipient: string;
  amountCents: Cents;
  /** Required for data: the bundle chosen. */
  productCode?: string | null;
}

/** Same checks as VasSale.Create on the server, with the same error codes, so the cashier hears about a mistake before the partner is called. */
export function checkVas(draft: VasDraft): Result<VasDraft & { recipient: string }> {
  const { min, max } = vasLimits[draft.product];
  if (draft.amountCents < min || draft.amountCents > max)
    return fail("vas.amount_invalid", `The amount must be between R${min / 100} and R${max / 100}.`);
  if (draft.amountCents % 100 !== 0) return fail("vas.amount_invalid", "The amount must be whole rand.");

  let recipient: string | null;
  if (draft.product === "electricity") {
    const meter = draft.recipient.replace(/[\s-]/g, "");
    recipient = /^\d{11}$/.test(meter) ? meter : null;
    if (recipient === null) return fail("vas.recipient_invalid", "A meter number is 11 digits.");
  } else {
    recipient = cleanPhone(draft.recipient.trim());
    if (recipient === null) return fail("vas.recipient_invalid", "That phone number is not valid.");
  }

  if (draft.product === "data" && !(draft.productCode ?? "").trim()) return fail("vas.product_required", "Choose a data bundle.");
  return ok({ ...draft, recipient });
}

/**
 * E10-1: airtime, data and electricity need the partner to answer, so they only work online.
 * Everything else at the till keeps working offline; this explains why this one button does not.
 */
export const vasUnavailableReason = (online: boolean): string | null =>
  online ? null : "Airtime, data and electricity need a network connection. Everything else still works.";
