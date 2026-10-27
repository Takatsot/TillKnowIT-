import type { Cents } from "./money";

/** Shops trade in South Africa, which has no daylight saving, so a business day is a fixed UTC+2 day. */
const OFFSET_HOURS = 2;
const HOUR_MS = 60 * 60 * 1000;

const pad = (n: number): string => String(n).padStart(2, "0");

/** The business day (yyyy-mm-dd) a moment falls on. */
export function businessDayOf(moment: Date): string {
  const local = new Date(moment.getTime() + OFFSET_HOURS * HOUR_MS);
  return `${local.getUTCFullYear()}-${pad(local.getUTCMonth() + 1)}-${pad(local.getUTCDate())}`;
}

/** The first moment of a business day. */
export function businessDayStart(day: string): Date {
  const [y, m, d] = day.split("-").map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d) - OFFSET_HOURS * HOUR_MS);
}

export interface CashSale {
  id: string;
  totalCents: Cents;
  soldAt: Date;
}

export interface CashPayment {
  amountCents: Cents;
  createdAt: Date;
}

/**
 * Cash taken at this till from the start of the business day to the moment of the cash-up:
 * cash sales that were not cancelled, plus cash paid on credit accounts. Cancelled sales are left out.
 */
export function cashTaken(input: {
  day: string;
  cashedUpAt: Date;
  sales: readonly CashSale[];
  cancelledSaleIds: ReadonlySet<string>;
  /** Credit payments made in cash, as signed entries (negative); they are counted as cash received. */
  creditCashPayments: readonly CashPayment[];
}): { cashSalesCents: Cents; creditCashCents: Cents } {
  const from = businessDayStart(input.day).getTime();
  const to = input.cashedUpAt.getTime();
  const within = (t: Date) => t.getTime() >= from && t.getTime() <= to;

  return {
    cashSalesCents: input.sales
      .filter((s) => within(s.soldAt) && !input.cancelledSaleIds.has(s.id))
      .reduce((sum, s) => sum + s.totalCents, 0),
    creditCashCents: input.creditCashPayments.filter((p) => within(p.createdAt)).reduce((sum, p) => sum - p.amountCents, 0),
  };
}

/** Float + cash sales + cash paid on credit accounts. */
export const expectedCash = (floatCents: Cents, cashSalesCents: Cents, creditCashCents: Cents): Cents =>
  floatCents + cashSalesCents + creditCashCents;

/** Counted minus expected. Negative means the till is short. */
export const cashDifference = (countedCents: Cents, expectedCents: Cents): Cents => countedCents - expectedCents;
