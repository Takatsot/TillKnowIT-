import { describe, expect, it } from "vitest";
import { MemoryOutbox, flushOutbox, outboxCounts, requeue, type SendBatch, type UploadRequest } from "./outbox";
import {
  cancellationRecord, cashUpRecord, creditCancelRecord, creditPaymentRecord, creditSaleRecord, queueBuilt, refundRecord, stockMovementRecord,
} from "./records";

const at = new Date("2026-10-26T08:00:00Z");
const ctx = (id: string) => ({ id, staffId: "staff-1", at });
const target = { shopId: "shop", deviceId: "till" };

describe("record builders", () => {
  it("builds a stock movement with the same checks as the server", () => {
    const received = stockMovementRecord({ productId: "p", type: "receive", quantity: 12, unitCostCents: 900, supplierName: " Acme ", invoiceRef: "INV-1" }, ctx("m1"));
    expect(received).toEqual({ ok: true, value: expect.objectContaining({ id: "m1", staffId: "staff-1", quantity: 12, supplierName: "Acme", invoiceRef: "INV-1", reasonCode: null, createdAt: at.toISOString() }) });

    expect(stockMovementRecord({ productId: "p", type: "write_off", quantity: 2, reasonCode: "damaged" }, ctx("m2"))).toMatchObject({ ok: false, code: "stock.quantity_sign" });
    expect(stockMovementRecord({ productId: "p", type: "write_off", quantity: -2 }, ctx("m3"))).toMatchObject({ ok: false, code: "stock.reason_required" });
    expect(stockMovementRecord({ productId: "p", type: "count", quantity: 0, reasonCode: "counted" }, ctx("m4"))).toMatchObject({ ok: false, code: "stock.quantity_zero" });
  });

  it("needs a reason for a cancellation and a refund, and something to refund", () => {
    expect(cancellationRecord({ saleId: "s", reason: "  " }, ctx("c1"))).toMatchObject({ ok: false, code: "cancellation.reason_required" });
    expect(cancellationRecord({ saleId: "s", reason: "Wrong item" }, ctx("c2"))).toMatchObject({ ok: true, value: { saleId: "s", reason: "Wrong item" } });

    expect(refundRecord({ saleId: "s", reason: "", lines: [{ productId: "p", quantity: 1 }] }, ctx("r1"))).toMatchObject({ ok: false, code: "refund.reason_required" });
    expect(refundRecord({ saleId: "s", reason: "Faulty", lines: [{ productId: "p", quantity: 0 }] }, ctx("r2"))).toMatchObject({ ok: false, code: "refund.no_lines" });
    expect(refundRecord({ saleId: "s", reason: "Faulty", lines: [{ productId: "p", quantity: 1 }, { productId: "p", quantity: 1 }] }, ctx("r3"))).toMatchObject({ ok: false, code: "refund.line_repeated" });
    expect(refundRecord({ saleId: "s", reason: "Faulty", approvedBy: "boss", lines: [{ productId: "p", quantity: 2 }, { productId: "q", quantity: 0 }] }, ctx("r4")))
      .toMatchObject({ ok: true, value: { methodCode: null, approvedBy: "boss", lines: [{ productId: "p", quantity: 2 }] } });
  });

  it("signs credit entries the way the server expects", () => {
    expect(creditSaleRecord({ accountId: "a", saleId: "s", totalCents: 5000 }, ctx("e1"))).toMatchObject({ ok: true, value: { type: "sale", amountCents: 5000, saleId: "s" } });
    expect(creditCancelRecord({ accountId: "a", saleId: "s", totalCents: 5000 }, ctx("e2"))).toMatchObject({ ok: true, value: { type: "cancel", amountCents: -5000 } });
    expect(creditPaymentRecord({ accountId: "a", amountCents: 2000, paymentMethodCode: "cash" }, ctx("e3"))).toMatchObject({ ok: true, value: { type: "payment", amountCents: -2000, saleId: null, paymentMethodCode: "cash" } });
    expect(creditPaymentRecord({ accountId: "a", amountCents: 2000, paymentMethodCode: "voucher" }, ctx("e4"))).toMatchObject({ ok: false, code: "credit.payment_method" });
    expect(creditPaymentRecord({ accountId: "a", amountCents: 0, paymentMethodCode: "cash" }, ctx("e5"))).toMatchObject({ ok: false });
  });

  it("builds a cash-up on the business day of the moment", () => {
    const built = cashUpRecord({ floatCents: 50_000, expectedCents: 120_000, countedCents: 119_000, note: " short " }, { id: "u1", staffId: "s", at: new Date("2026-10-26T23:30:00Z") });
    expect(built).toMatchObject({ ok: true, value: { day: "2026-10-27", countedCents: 119_000, note: "short" } });
    expect(cashUpRecord({ floatCents: -1, expectedCents: 0, countedCents: 0 }, ctx("u2"))).toMatchObject({ ok: false, code: "cashup.amount_invalid" });
  });
});

describe("the outbox with other kinds of record", () => {
  it("sends sales and other records together, each in its own list, and removes what the server accepted", async () => {
    const outbox = new MemoryOutbox();
    await queueBuilt(outbox, "stock_movement", stockMovementRecord({ productId: "p", type: "receive", quantity: 5 }, ctx("m1")), at);
    await queueBuilt(outbox, "cancellation", cancellationRecord({ saleId: "s", reason: "Mistake" }, ctx("c1")), at);
    await queueBuilt(outbox, "cash_up", cashUpRecord({ floatCents: 0, expectedCents: 0, countedCents: 0 }, ctx("u1")), at);
    const seen: UploadRequest[] = [];
    const send: SendBatch = async (request) => {
      seen.push(request);
      return { results: [{ kind: "stock_movement", id: "m1", status: "accepted" }, { kind: "cancellation", id: "c1", status: "duplicate" }, { kind: "cash_up", id: "u1", status: "accepted" }] };
    };

    const outcome = await flushOutbox(outbox, target, send);

    expect(outcome).toEqual({ sent: 3, rejected: 0, failed: false });
    expect(seen).toHaveLength(1);
    expect(seen[0]).toMatchObject({ shopId: "shop", deviceId: "till", sales: [], stockMovements: [{ id: "m1" }], cancellations: [{ id: "c1" }], cashUps: [{ id: "u1" }] });
    expect(seen[0]!.refunds).toBeUndefined();
    expect(await outbox.all()).toEqual([]);
  });

  it("matches the answer by kind and id, so a sale and a record that share an id never confuse each other", async () => {
    const outbox = new MemoryOutbox();
    await queueBuilt(outbox, "refund", refundRecord({ saleId: "s", reason: "Faulty", lines: [{ productId: "p", quantity: 1 }] }, ctx("same")), at);

    await flushOutbox(outbox, target, async () => ({ results: [{ kind: "sale", id: "same", status: "accepted" }] }));
    expect(await outboxCounts(outbox)).toEqual({ waiting: 1, refused: 0 });

    await flushOutbox(outbox, target, async () => ({ results: [{ kind: "refund", id: "same", status: "rejected", code: "refund.approval_required", message: "A manager must approve." }] }));
    expect((await outbox.all())[0]).toMatchObject({ state: "rejected", rejection: { code: "refund.approval_required" } });
  });

  it("requeues a rejected record with its fixed content", async () => {
    const outbox = new MemoryOutbox();
    const built = refundRecord({ saleId: "s", reason: "Faulty", lines: [{ productId: "p", quantity: 1 }] }, ctx("r1"));
    await queueBuilt(outbox, "refund", built, at);
    await flushOutbox(outbox, target, async () => ({ results: [{ kind: "refund", id: "r1", status: "rejected", code: "refund.approval_required", message: "x" }] }));
    const fixed = refundRecord({ saleId: "s", reason: "Faulty", approvedBy: "boss", lines: [{ productId: "p", quantity: 1 }] }, ctx("r1"));

    expect(await requeue(outbox, "r1", fixed.ok ? fixed.value : undefined)).toBe(true);

    expect((await outbox.all())[0]).toMatchObject({ state: "pending", record: { approvedBy: "boss" } });
  });

  it("does not queue a record that failed its checks", async () => {
    const outbox = new MemoryOutbox();

    const result = await queueBuilt(outbox, "stock_movement", stockMovementRecord({ productId: "p", type: "receive", quantity: -1 }, ctx("m1")), at);

    expect(result.ok).toBe(false);
    expect(await outbox.all()).toEqual([]);
  });
});
