// @vitest-environment jsdom
import { newId, queueSale } from "@tillknowit/domain";
import { describe, expect, it } from "vitest";
import { BookkeepingOutbox } from "./bookkeepingOutbox";
import { aSale } from "./coreTesting";
import { addRefunded, cashUpFigures, logSale, loggedSaleOf, recentSales, refundAmount, refundableQuantity } from "./ledger";
import { MemoryKeyValueStore } from "./localData";
import { MemoryOutbox } from "@tillknowit/domain";

describe("the till's books", () => {
  it("counts only the cash that stayed in the drawer: tendered less change, or the cash part of a split", () => {
    expect(loggedSaleOf(aSale()).cashCents).toBe(3_600);
    expect(loggedSaleOf(aSale({ paymentMethodCode: "card", tenderedCents: null, changeCents: null })).cashCents).toBe(0);
    expect(loggedSaleOf(aSale({ paymentMethodCode: "split", tenderedCents: null, changeCents: null, payments: [{ methodCode: "cash", amountCents: 1_000, reference: null, tenderedCents: 2_000 }, { methodCode: "card", amountCents: 2_600, reference: null, tenderedCents: null }] })).cashCents).toBe(1_000);
  });

  it("keeps only the newest 300 sales and logging a sale twice is one sale", async () => {
    const kv = new MemoryKeyValueStore();
    for (let i = 0; i < 305; i++) await logSale(kv, aSale({ id: `s${i}` }));
    await logSale(kv, aSale({ id: "s304" }));

    const sales = await recentSales(kv);
    expect(sales).toHaveLength(300);
    expect(sales[0]!.id).toBe("s5");
    expect(sales.filter((s) => s.id === "s304")).toHaveLength(1);
  });

  it("works out a refund as each line's share of what was paid, after discount, rounded half up", async () => {
    const kv = new MemoryKeyValueStore();
    await logSale(kv, aSale({ id: "x", lines: [{ productId: "p", name: "Milk", quantity: 3, priceCents: 1000, costCents: 500, unitDivisor: 1, discountCents: 1, promotionId: null }] }));
    const [sale] = await recentSales(kv);

    expect(refundAmount(sale!, [{ productId: "p", quantity: 1 }])).toBe(1000);   // 2999 / 3 = 999.67 -> 1000
    expect(refundAmount(sale!, [{ productId: "p", quantity: 3 }])).toBe(2999);
    await addRefunded(kv, "x", [{ productId: "p", quantity: 2 }]);
    const [after] = await recentSales(kv);
    expect(refundableQuantity(after!, after!.lines[0]!)).toBe(1);
  });

  it("books whatever is queued through the wrapper, once, and never loses a queued sale if the books fail", async () => {
    const kv = new MemoryKeyValueStore();
    const raw = new MemoryOutbox();
    const outbox = new BookkeepingOutbox(raw, kv);
    const sale = aSale({ id: newId(1) });

    await queueSale(outbox, sale, new Date());
    await queueSale(outbox, sale, new Date());
    expect(await recentSales(kv)).toHaveLength(1);

    const broken = new BookkeepingOutbox(raw, { ...kv, get: async () => { throw new Error("disk"); }, set: async () => { throw new Error("disk"); }, delete: async () => {}, clear: async () => {} });
    await queueSale(broken, aSale({ id: newId(2) }), new Date());
    expect(await raw.all()).toHaveLength(2);
  });

  it("works out the expected cash for one business day only, with the float and refunds", async () => {
    const kv = new MemoryKeyValueStore();
    await logSale(kv, aSale({ id: "today", soldAt: "2026-10-26T08:00:00.000Z" }));
    await logSale(kv, aSale({ id: "yesterday", soldAt: "2026-10-25T08:00:00.000Z" }));
    await kv.set("ledger.cash", [{ at: "2026-10-26T09:00:00.000Z", cents: -500, kind: "refund" }, { at: "2026-10-26T09:30:00.000Z", cents: 2_000, kind: "credit_payment" }]);

    const figures = await cashUpFigures(kv, "2026-10-26", 10_000, new Date("2026-10-26T15:00:00Z"));

    expect(figures).toMatchObject({ cashSalesCents: 3_600, creditCashCents: 2_000, cashRefundsCents: 500, expectedCents: 10_000 + 3_600 + 2_000 - 500 });
  });
});
