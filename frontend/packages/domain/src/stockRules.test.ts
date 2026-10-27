import { describe, expect, it } from "vitest";
import { isExpired } from "./product";
import { needsCount } from "./stock";
import {
  countAdjustment, daysLeft, filterStock, isExpiring, isNotSelling, isOut, isRunningLow, receiptMovements,
  scansToReceipt, stockFlags, suggestedOrderQuantity, type StockFilter, type StockItem,
} from "./stockRules";

const NOW = new Date("2026-10-26T10:00:00Z");
const TODAY = "2026-10-26";
const daysAgo = (n: number) => new Date(NOW.getTime() - n * 24 * 60 * 60 * 1000);

const item = (over: Partial<StockItem> = {}): StockItem => ({
  productId: crypto.randomUUID(), name: "Bread", barcode: "6001234567890", isActive: true, addedAt: daysAgo(90),
  onHand: 20, lowStockLevel: 5, expiryDate: null, soldLast30Days: 0, lastSoldAt: null, ...over,
});

// These cases deliberately repeat the C# StockTests so both implementations are held to the same numbers.
describe("stock rules (same as the server)", () => {
  it("out and running low", () => {
    expect(isOut(item({ onHand: 0 }))).toBe(true);
    expect(isOut(item({ onHand: -3 }))).toBe(true);
    expect(isOut(item({ onHand: 1 }))).toBe(false);
    expect(isRunningLow(item({ onHand: 5, lowStockLevel: 5 }))).toBe(true);
    expect(isRunningLow(item({ onHand: 6, lowStockLevel: 5 }))).toBe(false);
    expect(isRunningLow(item({ onHand: 0, lowStockLevel: 5 }))).toBe(false);
    expect(isRunningLow(item({ onHand: 2, lowStockLevel: 0 }))).toBe(false);
  });

  it("not selling", () => {
    expect(isNotSelling(item({ lastSoldAt: daysAgo(31) }), NOW)).toBe(true);
    expect(isNotSelling(item({ lastSoldAt: null }), NOW)).toBe(true);
    expect(isNotSelling(item({ lastSoldAt: daysAgo(29) }), NOW)).toBe(false);
    expect(isNotSelling(item({ onHand: 0 }), NOW)).toBe(false);
    expect(isNotSelling(item({ addedAt: daysAgo(10) }), NOW)).toBe(false);
  });

  it("expiring includes already expired, and only while there is stock", () => {
    expect(isExpiring(item({ expiryDate: "2026-11-09" }), TODAY)).toBe(true);
    expect(isExpiring(item({ expiryDate: "2026-11-10" }), TODAY)).toBe(false);
    expect(isExpiring(item({ expiryDate: "2026-10-25" }), TODAY)).toBe(true);
    expect(isExpiring(item({ onHand: 0, expiryDate: "2026-10-27" }), TODAY)).toBe(false);
    expect(isExpired("2026-10-25", TODAY)).toBe(true);
    expect(isExpired(TODAY, TODAY)).toBe(false);
  });

  it("days left and suggested order", () => {
    const steady = item({ onHand: 10, soldLast30Days: 30 });
    expect(daysLeft(steady)).toBe(10);
    expect(suggestedOrderQuantity(steady)).toBe(4);
    expect(daysLeft(item({ onHand: 10, soldLast30Days: 0 }))).toBeNull();
    expect(daysLeft(item({ onHand: 0, soldLast30Days: 30 }))).toBeNull();
    expect(suggestedOrderQuantity(item({ onHand: -5, soldLast30Days: 30 }))).toBe(14);
    expect(suggestedOrderQuantity(item({ onHand: 50, soldLast30Days: 30 }))).toBe(0);
    expect(suggestedOrderQuantity(item({ onHand: 0, soldLast30Days: 10 }))).toBe(5);
    expect(needsCount(-1)).toBe(true);
  });

  it("flags", () => {
    expect(stockFlags(item({ onHand: -2 }), NOW, TODAY)).toEqual(["out", "needs_count"]);
    expect(stockFlags(item({ expiryDate: "2026-10-29", lastSoldAt: NOW }), NOW, TODAY)).toEqual(["expiring"]);
  });
});

describe("the stock list", () => {
  const shelf = (): StockItem[] => [
    item({ name: "Bread", onHand: 3, lowStockLevel: 5, soldLast30Days: 60, lastSoldAt: daysAgo(1) }),
    item({ name: "Airtime voucher", onHand: 0, lowStockLevel: 5 }),
    item({ name: "Old tins", onHand: 12, lowStockLevel: 2, lastSoldAt: daysAgo(60) }),
    item({ name: "Milk", onHand: 20, expiryDate: "2026-10-29", soldLast30Days: 30, lastSoldAt: NOW }),
    item({ name: "Rice", onHand: 40, soldLast30Days: 30, lastSoldAt: NOW }),
    item({ name: "Switched off", onHand: 0, isActive: false }),
  ];
  const names = (f: StockFilter) => filterStock(shelf(), f, NOW).map((i) => i.name);

  it("each filter shows the same products as the server, in name order", () => {
    expect(names("all")).toEqual(["Airtime voucher", "Bread", "Milk", "Old tins", "Rice"]);
    expect(names("low")).toEqual(["Bread"]);
    expect(names("out")).toEqual(["Airtime voucher"]);
    expect(names("not_selling")).toEqual(["Old tins"]);
    expect(names("expiring")).toEqual(["Milk"]);
  });

  it("uses the South African day: 23:30 UTC on the 26th is already the 27th", () => {
    const late = new Date("2026-10-26T23:30:00Z");
    const yoghurt = item({ name: "Yoghurt", expiryDate: "2026-11-10" });
    expect(filterStock([yoghurt], "expiring", late)).toHaveLength(1);
    expect(filterStock([yoghurt], "expiring", NOW)).toHaveLength(0);
  });
});

describe("counting", () => {
  it("a count becomes the movement that fixes the books", () => {
    expect(countAdjustment(10, 10, "counted")).toBeNull();
    expect(countAdjustment(10, 12, "counted")).toEqual({ type: "count", quantity: 2 });
    expect(countAdjustment(10, 7, "counted")).toEqual({ type: "count", quantity: -3 });
    expect(countAdjustment(10, 7, "damaged")).toEqual({ type: "write_off", quantity: -3 });
    expect(countAdjustment(10, 12, "damaged")).toEqual({ type: "count", quantity: 2 });
    expect(countAdjustment(-4, 0, "counted")).toEqual({ type: "count", quantity: 4 });
  });
});

describe("receiving stock by scanning", () => {
  const products = new Map([
    ["6001111111111", { id: "p-bread", name: "Bread" }],
    ["6002222222222", { id: "p-milk", name: "Milk" }],
  ]);

  it("each scan adds one, grouped by product in the order first scanned", () => {
    const { lines, unknown } = scansToReceipt(["6002222222222", "6001111111111", "6002222222222", " 6002222222222 "], products);
    expect(lines).toEqual([
      { productId: "p-milk", name: "Milk", quantity: 3 },
      { productId: "p-bread", name: "Bread", quantity: 1 },
    ]);
    expect(unknown.size).toBe(0);
  });

  it("keeps unknown barcodes apart instead of losing them", () => {
    const { lines, unknown } = scansToReceipt(["6009999999999", "6009999999999", "6001111111111"], products);
    expect(lines).toHaveLength(1);
    expect(unknown.get("6009999999999")).toBe(2);
  });

  it("makes one receive movement per product sharing supplier and invoice", () => {
    const r = receiptMovements([{ productId: "p-bread", quantity: 24, unitCostCents: 1400 }, { productId: "p-milk", quantity: 12, unitCostCents: 1900 }],
      " Makro ", " INV-1 ");
    expect(r.ok).toBe(true);
    if (r.ok) {
      expect(r.movements.map((m) => [m.productId, m.quantity, m.supplierName, m.invoiceRef])).toEqual([
        ["p-bread", 24, "Makro", "INV-1"], ["p-milk", 12, "Makro", "INV-1"],
      ]);
    }
  });

  it("refuses an empty receipt, bad quantities, negative costs and runaway text", () => {
    const code = (r: ReturnType<typeof receiptMovements>) => (r.ok ? "ok" : r.code);
    expect(code(receiptMovements([], null, null))).toBe("receipt.no_lines");
    expect(code(receiptMovements([{ productId: "p", quantity: 0, unitCostCents: 1 }], null, null))).toBe("stock.quantity_sign");
    expect(code(receiptMovements([{ productId: "p", quantity: 1.5, unitCostCents: 1 }], null, null))).toBe("stock.quantity_sign");
    expect(code(receiptMovements([{ productId: "p", quantity: 1, unitCostCents: -1 }], null, null))).toBe("stock.cost_invalid");
    expect(code(receiptMovements([{ productId: "p", quantity: 1, unitCostCents: 1 }], "x".repeat(201), null))).toBe("stock.text_too_long");
    expect(code(receiptMovements([{ productId: "p", quantity: 1, unitCostCents: 1 }], null, null))).toBe("ok");
  });
});
