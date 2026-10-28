import { describe, expect, it } from "vitest";
import type { SaleUploadDto } from "./basket";
import { MAX_BATCH, MemoryOutbox, flushOutbox, outboxCounts, queueSale, requeue, retryDelaySeconds, type SendBatch, type UploadRecordResult } from "./outbox";

const sale = (id: string): SaleUploadDto => ({
  id, staffId: "s", soldAt: "2026-10-26T08:00:00.000Z", paymentMethodCode: "cash", totalCents: 100, vatCents: 13, tenderedCents: 100, changeCents: 0, accountId: null,
  lines: [{ productId: "p", name: "Bread", quantity: 1, priceCents: 100, costCents: 50, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
});

const target = { shopId: "shop", deviceId: "till" };
const answer = (...results: Array<[string, UploadRecordResult["status"], string?]>): SendBatch =>
  async () => ({ results: results.map(([id, status, code]) => ({ kind: "sale", id, status, code: code ?? null, message: code ? `because ${code}` : null })) });

describe("the sales queue", () => {
  it("keeps a sale the moment it is queued and queuing it twice is one sale", async () => {
    const outbox = new MemoryOutbox();

    await queueSale(outbox, sale("a"), new Date("2026-10-26T08:00:00Z"));
    await queueSale(outbox, sale("a"), new Date("2026-10-26T08:05:00Z"));

    expect(await outboxCounts(outbox)).toEqual({ waiting: 1, refused: 0 });
  });

  it("removes accepted and duplicate sales and keeps a rejected one with the servers reason", async () => {
    const outbox = new MemoryOutbox();
    for (const id of ["a", "b", "c"]) await queueSale(outbox, sale(id), new Date(`2026-10-26T08:0${id === "a" ? 0 : id === "b" ? 1 : 2}:00Z`));

    const outcome = await flushOutbox(outbox, target, answer(["a", "accepted"], ["b", "duplicate"], ["c", "rejected", "sale.total_mismatch"]));

    expect(outcome).toEqual({ sent: 2, rejected: 1, failed: false });
    const left = await outbox.all();
    expect(left.map((e) => [e.id, e.state, e.rejection?.code])).toEqual([["c", "rejected", "sale.total_mismatch"]]);
    expect(await outboxCounts(outbox)).toEqual({ waiting: 0, refused: 1 });
  });

  it("does not send a rejected sale again until it is fixed and requeued", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("c"), new Date());
    await flushOutbox(outbox, target, answer(["c", "rejected", "sale.vat_invalid"]));
    let calls = 0;
    const counting: SendBatch = async (r) => { calls++; return { results: r.sales.map((s) => ({ kind: "sale", id: s.id, status: "accepted" as const })) }; };

    await flushOutbox(outbox, target, counting);
    expect(calls).toBe(0);

    expect(await requeue(outbox, "c", { ...sale("c"), vatCents: 10 })).toBe(true);
    expect(await requeue(outbox, "c")).toBe(false);   // it is pending now, not rejected
    await flushOutbox(outbox, target, counting);
    expect(calls).toBe(1);
    expect(await outbox.all()).toEqual([]);
  });

  it("keeps everything and counts the attempt when the connection fails", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());

    const outcome = await flushOutbox(outbox, target, async () => { throw new Error("offline"); });

    expect(outcome).toEqual({ sent: 0, rejected: 0, failed: true });
    expect((await outbox.all())[0]).toMatchObject({ id: "a", state: "pending", attempts: 1 });
  });

  it("keeps a sale the server did not mention for the next round", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());

    await flushOutbox(outbox, target, async () => ({ results: [] }));

    expect((await outbox.all())[0]).toMatchObject({ state: "pending", attempts: 1 });
  });

  it("sends oldest first in batches of 500", async () => {
    const outbox = new MemoryOutbox();
    for (let i = 0; i < MAX_BATCH + 20; i++) await queueSale(outbox, sale(`s${String(i).padStart(4, "0")}`), new Date(Date.UTC(2026, 9, 26, 8, 0, 0, i)));
    const sizes: number[] = [];

    await flushOutbox(outbox, target, async (r) => {
      sizes.push(r.sales.length);
      return { results: r.sales.map((s) => ({ kind: "sale", id: s.id, status: "accepted" as const })) };
    });

    expect(sizes).toEqual([500, 20]);
    expect(await outbox.all()).toEqual([]);
  });

  it("backs off 5, 10, 20 seconds and no further than five minutes", () => {
    expect([1, 2, 3, 4, 5, 6, 7, 50].map(retryDelaySeconds)).toEqual([5, 10, 20, 40, 80, 160, 300, 300]);
    expect(retryDelaySeconds(0)).toBe(5);
  });
});
