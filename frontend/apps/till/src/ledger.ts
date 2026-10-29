import { businessDayStart, cashDifference, cashTaken, expectedCash, type Cents, type SaleUploadDto } from "@tillknowit/domain";
import type { KeyValueStore } from "./localData";

/**
 * The till's own books: the sales rung up here recently, and the cash that came in or went out apart from sales. Refunds, cancellations and the cash-up work from
 * these, so they work with no connection. Only the newest sales are kept: the server has the rest.
 */

export interface LoggedLine {
  productId: string;
  name: string;
  quantity: number;
  unitDivisor: number;
  /** What the customer paid for the line, after any discount. */
  totalCents: Cents;
}

export interface LoggedSale {
  id: string;
  soldAt: string;
  staffId: string;
  paymentMethodCode: string;
  totalCents: Cents;
  tipCents: Cents;
  /** Cash that stayed in the drawer for this sale: what was handed over less the change. */
  cashCents: Cents;
  accountId: string | null;
  lines: LoggedLine[];
  cancelled: boolean;
  /** Quantity already taken back, by product. */
  refunded: Record<string, number>;
}

export interface CashMovement {
  at: string;
  /** Positive: cash in. Negative: cash out. */
  cents: Cents;
  /** What it was, for the cash-up: a credit payment, or a refund. */
  kind: "credit_payment" | "refund";
}

const SALES_KEY = "ledger.sales";
const CASH_KEY = "ledger.cash";
const LAST_CASHUP_KEY = "ledger.lastCashUp";
const KEEP_SALES = 300;
const KEEP_MOVEMENTS = 500;

export function loggedSaleOf(sale: SaleUploadDto): LoggedSale {
  const cashCents = sale.payments
    ? sale.payments.filter((p) => p.methodCode === "cash").reduce((sum, p) => sum + p.amountCents, 0)
    : sale.paymentMethodCode === "cash" ? (sale.tenderedCents ?? 0) - (sale.changeCents ?? 0) : 0;
  return {
    id: sale.id, soldAt: sale.soldAt, staffId: sale.staffId, paymentMethodCode: sale.paymentMethodCode, totalCents: sale.totalCents, tipCents: sale.tipCents, cashCents,
    accountId: sale.accountId,
    lines: sale.lines.map((l) => ({ productId: l.productId, name: l.name, quantity: l.quantity, unitDivisor: l.unitDivisor, totalCents: lineAfterDiscount(l) })),
    cancelled: false, refunded: {},
  };
}

function lineAfterDiscount(l: SaleUploadDto["lines"][number]): Cents {
  const gross = l.unitDivisor <= 1 ? l.priceCents * l.quantity : Math.floor((l.priceCents * l.quantity + l.unitDivisor / 2) / l.unitDivisor);
  return gross - l.discountCents;
}

export async function recentSales(kv: KeyValueStore): Promise<LoggedSale[]> {
  return (await kv.get<LoggedSale[]>(SALES_KEY)) ?? [];
}

export async function logSale(kv: KeyValueStore, sale: SaleUploadDto): Promise<void> {
  const all = (await recentSales(kv)).filter((s) => s.id !== sale.id);
  await kv.set(SALES_KEY, [...all, loggedSaleOf(sale)].slice(-KEEP_SALES));
}

async function updateSale(kv: KeyValueStore, id: string, change: (s: LoggedSale) => LoggedSale): Promise<void> {
  await kv.set(SALES_KEY, (await recentSales(kv)).map((s) => (s.id === id ? change(s) : s)));
}

export const markCancelled = (kv: KeyValueStore, id: string): Promise<void> => updateSale(kv, id, (s) => ({ ...s, cancelled: true }));

export const addRefunded = (kv: KeyValueStore, id: string, lines: ReadonlyArray<{ productId: string; quantity: number }>): Promise<void> =>
  updateSale(kv, id, (s) => {
    const refunded = { ...s.refunded };
    for (const l of lines) refunded[l.productId] = (refunded[l.productId] ?? 0) + l.quantity;
    return { ...s, refunded };
  });

export async function cashMovements(kv: KeyValueStore): Promise<CashMovement[]> {
  return (await kv.get<CashMovement[]>(CASH_KEY)) ?? [];
}

export async function logCash(kv: KeyValueStore, movement: CashMovement): Promise<void> {
  await kv.set(CASH_KEY, [...(await cashMovements(kv)), movement].slice(-KEEP_MOVEMENTS));
}

/** How much of a line can still be taken back. */
export const refundableQuantity = (sale: LoggedSale, line: LoggedLine): number => Math.max(0, line.quantity - (sale.refunded[line.productId] ?? 0));

/** The money a refund of these quantities gives back: each line's share of what was paid, rounded half up like the server. */
export function refundAmount(sale: LoggedSale, asked: ReadonlyArray<{ productId: string; quantity: number }>): Cents {
  return asked.reduce((sum, a) => {
    const line = sale.lines.find((l) => l.productId === a.productId);
    return line && a.quantity > 0 ? sum + Math.floor((line.totalCents * a.quantity + line.quantity / 2) / line.quantity) : sum;
  }, 0);
}

export interface CashUpFigures {
  day: string;
  floatCents: Cents;
  cashSalesCents: Cents;
  creditCashCents: Cents;
  cashRefundsCents: Cents;
  expectedCents: Cents;
}

/**
 * What should be in the drawer at this moment of the business day: the float, plus cash sales that were not cancelled, plus cash paid on credit accounts, less cash
 * given back in refunds. Uses the domain's cash-up rules.
 */
export async function cashUpFigures(kv: KeyValueStore, day: string, floatCents: Cents, at: Date): Promise<CashUpFigures> {
  const sales = await recentSales(kv);
  const movements = await cashMovements(kv);
  const taken = cashTaken({
    day, cashedUpAt: at,
    sales: sales.map((s) => ({ id: s.id, totalCents: s.cashCents, soldAt: new Date(s.soldAt) })),
    cancelledSaleIds: new Set(sales.filter((s) => s.cancelled).map((s) => s.id)),
    // The domain counts credit payments as signed entries (negative), so the till's cash-in is passed negated.
    creditCashPayments: movements.filter((m) => m.kind === "credit_payment").map((m) => ({ amountCents: -m.cents, createdAt: new Date(m.at) })),
  });
  const from = businessDayStart(day).getTime();
  const cashRefundsCents = movements.filter((m) => m.kind === "refund" && new Date(m.at).getTime() >= from && new Date(m.at).getTime() <= at.getTime()).reduce((sum, m) => sum - m.cents, 0);
  return { day, floatCents, cashSalesCents: taken.cashSalesCents, creditCashCents: taken.creditCashCents, cashRefundsCents, expectedCents: expectedCash(floatCents, taken.cashSalesCents, taken.creditCashCents) - cashRefundsCents };
}

export { cashDifference };

export const lastCashUpDay = (kv: KeyValueStore): Promise<string | undefined> => kv.get<string>(LAST_CASHUP_KEY);
export const setLastCashUpDay = (kv: KeyValueStore, day: string): Promise<void> => kv.set(LAST_CASHUP_KEY, day);
