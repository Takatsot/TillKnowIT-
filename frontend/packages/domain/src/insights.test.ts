import { describe, expect, it } from "vitest";
import { bestSellers, periodStart, summarise, type SaleLineRow } from "./insights";

// The same data and the same expected numbers as InsightsAndReportsTests in C#.
const NOW = new Date("2026-10-26T10:00:00Z");
const ago = (hours: number) => new Date(NOW.getTime() - hours * 3_600_000);

const line = (saleId: string, productId: string, name: string, quantity: number, priceCents: number, costCents: number, soldAt: Date, cancelled = false): SaleLineRow =>
  ({ saleId, productId, name, quantity, priceCents, costCents, soldAt, cancelled });

const rows = (): SaleLineRow[] => [
  line("s1", "bread", "Bread", 2, 1800, 1400, ago(1)),
  line("s1", "milk", "Milk", 1, 2500, 2100, ago(1)),
  line("s2", "bread", "Bread", 1, 1800, 1400, ago(3 * 24)),
  line("s3", "milk", "Milk", 4, 2500, 2100, ago(20 * 24)),
  line("s4", "bread", "Bread", 9, 1800, 1400, ago(2), true),
  line("s5", "bread", "Bread", 1, 1800, 1400, ago(45 * 24)),
];

describe("insights (same numbers as the server)", () => {
  it("periods start at the beginning of the business day", () => {
    expect(periodStart(NOW, 1).toISOString()).toBe("2026-10-25T22:00:00.000Z");
    expect(periodStart(NOW, 7).toISOString()).toBe("2026-10-19T22:00:00.000Z");
  });

  it("sales, profit, customers and average sale, ignoring cancelled sales", () => {
    expect(summarise(rows(), NOW, 1)).toEqual({ days: 1, salesCents: 6100, profitCents: 1200, customers: 1, averageSaleCents: 6100 });
    expect(summarise(rows(), NOW, 7)).toMatchObject({ salesCents: 7900, customers: 2, averageSaleCents: 3950 });
    expect(summarise(rows(), NOW, 30)).toMatchObject({ salesCents: 17900, customers: 3 });
  });

  it("an empty period is zeros, and the average rounds half up", () => {
    expect(summarise([], NOW, 7)).toEqual({ days: 7, salesCents: 0, profitCents: 0, customers: 0, averageSaleCents: 0 });
    const odd = summarise([line("a", "p", "B", 1, 100, 0, NOW), line("b", "p", "B", 1, 101, 0, NOW)], NOW, 1);
    expect(odd.averageSaleCents).toBe(101);
  });

  it("best sellers rank by units, then revenue, then name", () => {
    const best = bestSellers(rows(), NOW, 30, 10);
    expect(best.map((b) => b.name)).toEqual(["Milk", "Bread"]);
    expect(best[0]?.units).toBe(5);
    expect(best[1]?.units).toBe(3);
    expect(bestSellers(rows(), NOW, 30, 1)).toHaveLength(1);
  });
});
