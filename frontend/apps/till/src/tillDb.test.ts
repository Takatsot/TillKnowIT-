import "fake-indexeddb/auto";
import { cancellationRecord, queueBuilt, queueSale, stockMovementRecord, type SaleUploadDto } from "@tillknowit/domain";
import { beforeEach, describe, expect, it } from "vitest";
import { IndexedDbOutbox } from "./indexedDbOutbox";
import { IndexedDbKeyValueStore } from "./localData";
import { DB_NAME } from "./tillDb";

const sale = (id: string): SaleUploadDto => ({
  id, staffId: "s", soldAt: "2026-10-26T08:00:00.000Z", paymentMethodCode: "card", totalCents: 100, vatCents: 13, tenderedCents: null, changeCents: null, accountId: null,
  lines: [{ productId: "p", name: "Bread", quantity: 1, priceCents: 100, costCents: 50, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
});

beforeEach(async () => {
  await new IndexedDbKeyValueStore().clear();
  const outbox = new IndexedDbOutbox();
  for (const e of await outbox.all()) await outbox.remove(e.id);
});

describe("IndexedDbKeyValueStore", () => {
  it("keeps json under a key, survives a new instance, and deletes and clears", async () => {
    const kv = new IndexedDbKeyValueStore();
    await kv.set("a", { n: 1, list: ["x"] });
    await kv.set("b", "text");

    expect(await new IndexedDbKeyValueStore().get("a")).toEqual({ n: 1, list: ["x"] });
    expect(await kv.get("missing")).toBeUndefined();
    await kv.delete("b");
    expect(await kv.get("b")).toBeUndefined();
    await kv.clear();
    expect(await kv.get("a")).toBeUndefined();
  });
});

describe("IndexedDbOutbox with every kind of record", () => {
  it("keeps sales and other records side by side, oldest first, and updates and removes either", async () => {
    const outbox = new IndexedDbOutbox();
    await queueSale(outbox, sale("sale-1"), new Date("2026-10-26T08:00:00Z"));
    await queueBuilt(outbox, "stock_movement", stockMovementRecord({ productId: "p", type: "receive", quantity: 4 }, { id: "move-1", staffId: "s", at: new Date() }), new Date("2026-10-26T08:01:00Z"));
    await queueBuilt(outbox, "cancellation", cancellationRecord({ saleId: "sale-1", reason: "Mistake" }, { id: "cancel-1", staffId: "s", at: new Date() }), new Date("2026-10-26T08:02:00Z"));

    const all = await outbox.all();
    expect(all.map((e) => [e.id, e.kind ?? "sale"])).toEqual([["sale-1", "sale"], ["move-1", "stock_movement"], ["cancel-1", "cancellation"]]);

    await outbox.update({ ...all[1]!, state: "rejected", rejection: { code: "stock.reason_invalid", message: "Unknown reason." } });
    await outbox.remove("sale-1");
    expect((await outbox.all()).map((e) => [e.id, e.state])).toEqual([["move-1", "rejected"], ["cancel-1", "pending"]]);
  });
});

describe("upgrading a till that only had the sales queue", () => {
  it("keeps the queued sales when the key-value store is added", async () => {
    // A version 1 database, as the till had before downloads existed, with one sale waiting.
    await new Promise<void>((resolve, reject) => {
      const del = indexedDB.deleteDatabase(DB_NAME);
      del.onsuccess = () => resolve();
      del.onerror = () => reject(del.error);
    });
    await new Promise<void>((resolve, reject) => {
      const open = indexedDB.open(DB_NAME, 1);
      open.onupgradeneeded = () => { open.result.createObjectStore("outbox", { keyPath: "id" }).add({ id: "old-sale", sale: sale("old-sale"), state: "pending", attempts: 2, queuedAt: "2026-10-25T08:00:00.000Z" }); };
      open.onsuccess = () => { open.result.close(); resolve(); };
      open.onerror = () => reject(open.error);
    });

    const outbox = new IndexedDbOutbox();
    const kv = new IndexedDbKeyValueStore();
    await kv.set("staff", [1]);

    expect((await outbox.all()).map((e) => e.id)).toEqual(["old-sale"]);
    expect(await kv.get("staff")).toEqual([1]);
  });
});
