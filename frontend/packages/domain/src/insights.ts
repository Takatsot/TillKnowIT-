import { businessDayOf, businessDayStart } from "./cashup";
import type { Cents } from "./money";

/**
 * The owner's numbers, worked out on the till from the last 90 days of local sales so they work offline.
 * These match InsightsCalculator in C#: a "customer" is one completed sale, and cancelled sales count for nothing.
 */
export interface SaleLineRow {
  saleId: string;
  soldAt: Date;
  productId: string;
  name: string;
  quantity: number;
  priceCents: Cents;
  costCents: Cents;
  cancelled: boolean;
}

export interface PeriodSummary {
  days: number;
  salesCents: Cents;
  profitCents: Cents;
  customers: number;
  averageSaleCents: Cents;
}

export interface BestSeller {
  productId: string;
  name: string;
  units: number;
  revenueCents: Cents;
  profitCents: Cents;
}

const addDays = (isoDay: string, days: number): string => {
  const [y, m, d] = isoDay.split("-").map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
};

/** The first moment of the period: 1 day is today, 7 days is today and the six before it. */
export const periodStart = (now: Date, days: number): Date => businessDayStart(addDays(businessDayOf(now), -(days - 1)));

const live = (rows: readonly SaleLineRow[], from: Date, to: Date) =>
  rows.filter((r) => !r.cancelled && r.soldAt.getTime() >= from.getTime() && r.soldAt.getTime() <= to.getTime());

export function summarise(rows: readonly SaleLineRow[], now: Date, days: number): PeriodSummary {
  const inPeriod = live(rows, periodStart(now, days), now);
  const salesCents = inPeriod.reduce((sum, r) => sum + r.priceCents * r.quantity, 0);
  const profitCents = inPeriod.reduce((sum, r) => sum + (r.priceCents - r.costCents) * r.quantity, 0);
  const customers = new Set(inPeriod.map((r) => r.saleId)).size;
  // Nearest cent, halves up, like the server.
  const averageSaleCents = customers === 0 ? 0 : Math.floor((salesCents * 2 + customers) / (customers * 2));
  return { days, salesCents, profitCents, customers, averageSaleCents };
}

/** Top sellers by units, then revenue, then name, so the order never jumps about. */
export function bestSellers(rows: readonly SaleLineRow[], now: Date, days: number, top: number): BestSeller[] {
  const byProduct = new Map<string, { latest: SaleLineRow; units: number; revenue: number; profit: number }>();
  for (const r of live(rows, periodStart(now, days), now)) {
    const entry = byProduct.get(r.productId) ?? { latest: r, units: 0, revenue: 0, profit: 0 };
    if (r.soldAt.getTime() > entry.latest.soldAt.getTime()) entry.latest = r;
    entry.units += r.quantity;
    entry.revenue += r.priceCents * r.quantity;
    entry.profit += (r.priceCents - r.costCents) * r.quantity;
    byProduct.set(r.productId, entry);
  }
  return [...byProduct.entries()]
    .map(([productId, e]) => ({ productId, name: e.latest.name, units: e.units, revenueCents: e.revenue, profitCents: e.profit }))
    .sort((a, b) => b.units - a.units || b.revenueCents - a.revenueCents || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))
    .slice(0, top);
}
