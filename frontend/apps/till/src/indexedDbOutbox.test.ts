import "fake-indexeddb/auto";
import { queueSale, type SaleUploadDto } from "@tillknowit/domain";
import { beforeEach, describe, expect, it } from "vitest";
import { IndexedDbOutbox } from "./indexedDbOutbox";

const sale = (id: string): SaleUploadDto => ({
  id, staffId: "s", soldAt: "2026-10-26T08:00:00.000Z", paymentMethodCode: "card", totalCents: 100, vatCents: 13, tenderedCents: null, changeCents: null, accountId: null,
  lines: [{ productId: "p", name: "Bread", quantity: 1, priceCents: 100, costCents: 50, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
});

beforeEach(async () => {
  const outbox = new IndexedDbOutbox();
  for (const e of await outbox.all()) await outbox.remove(e.id);
});

describe("IndexedDbOutbox", () => {
  it("keeps what was queued and a second queue of the same sale is the same sale", async () => {
    const outbox = new IndexedDbOutbox();

    await queueSale(outbox, sale("a"), new Date("2026-10-26T08:00:00Z"));
    await queueSale(outbox, sale("a"), new Date("2026-10-26T09:00:00Z"));

    const all = await outbox.all();
    expect(all).toHaveLength(1);
    expect(all[0]).toMatchObject({ id: "a", state: "pending", queuedAt: "2026-10-26T08:00:00.000Z" });
  });

  it("survives a new instance, as a restart would", async () => {
    await queueSale(new IndexedDbOutbox(), sale("a"), new Date());

    expect((await new IndexedDbOutbox().all()).map((e) => e.id)).toEqual(["a"]);
  });

  it("lists oldest first, updates in place and removes", async () => {
    const outbox = new IndexedDbOutbox();
    await queueSale(outbox, sale("late"), new Date("2026-10-26T09:00:00Z"));
    await queueSale(outbox, sale("early"), new Date("2026-10-26T08:00:00Z"));

    expect((await outbox.all()).map((e) => e.id)).toEqual(["early", "late"]);
    const [first] = await outbox.all();
    await outbox.update({ ...first!, state: "rejected", rejection: { code: "x", message: "y" } });
    expect((await outbox.all())[0]).toMatchObject({ id: "early", state: "rejected" });
    await outbox.remove("early");
    expect((await outbox.all()).map((e) => e.id)).toEqual(["late"]);
  });
});
