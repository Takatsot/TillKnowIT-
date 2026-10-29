/**
 * The shop's lay-by terms, from the pack's settings JSON. The server enforces them (LaybyTerms in C#); the till reads the same settings only to show the person
 * what will be asked of them before they press the button, and shows the server's own answer after. The defaults are the server's defaults.
 */
export interface LaybyTerms {
  minDepositPercent: number;
  maxWeeks: number;
  cancelFeePercent: number;
  minCancelFeeCents: number;
}

export const defaultTerms: LaybyTerms = { minDepositPercent: 20, maxWeeks: 12, cancelFeePercent: 10, minCancelFeeCents: 2_000 };

export function parseTerms(json: string | null): LaybyTerms {
  if (!json || json.trim() === "") return defaultTerms;
  try {
    const raw = JSON.parse(json) as Record<string, unknown>;
    const int = (name: string, fallback: number, min: number, max: number) => {
      const v = raw[name];
      return typeof v === "number" && Number.isInteger(v) && v >= min && v <= max ? v : fallback;
    };
    const cents = (name: string, fallback: number) => {
      const v = raw[name];
      return typeof v === "number" && Number.isInteger(v) && v >= 0 ? v : fallback;
    };
    return {
      minDepositPercent: int("minDepositPercent", defaultTerms.minDepositPercent, 0, 100),
      maxWeeks: int("maxWeeks", defaultTerms.maxWeeks, 1, 52),
      cancelFeePercent: int("cancelFeePercent", defaultTerms.cancelFeePercent, 0, 100),
      minCancelFeeCents: cents("minCancelFeeCents", defaultTerms.minCancelFeeCents),
    };
  } catch {
    return defaultTerms;
  }
}

/** The smallest deposit, rounded up so it is never under the percentage. */
export const minimumDeposit = (totalCents: number, terms: LaybyTerms): number => Math.floor((totalCents * terms.minDepositPercent + 99) / 100);

/** What the shop would keep if the lay-by were cancelled now: the percentage of the total, at least the minimum, never more than was paid. */
export function cancellationFee(totalCents: number, paidCents: number, terms: LaybyTerms): number {
  return Math.min(paidCents, Math.max(terms.minCancelFeeCents, Math.floor((totalCents * terms.cancelFeePercent + 50) / 100)));
}
