import type { Api } from "@tillknowit/api-client";
import { fail, MemoryOutbox, newId, ok, queueSale } from "@tillknowit/domain";
import { describe, expect, it, vi } from "vitest";
import { aSale, DEVICE, SHOP, STAFF } from "./coreTesting";
import { MemoryKeyValueStore } from "./localData";
import { pendingSerials, queuePendingSerials, sendPendingSerials } from "./pendingSerials";

const caller = { shopId: SHOP, deviceId: DEVICE, staffId: STAFF };

async function setup(post: ReturnType<typeof vi.fn>) {
  const kv = new MemoryKeyValueStore();
  const outbox = new MemoryOutbox();
  return { kv, outbox, deps: { kv, outbox, caller, api: { post } as unknown as Api } };
}

describe("serial numbers waiting for their sale", () => {
  it("keeps one set per sale, replacing what was typed before", async () => {
    const kv = new MemoryKeyValueStore();
    await queuePendingSerials(kv, "s1", [{ productId: "p1", serial: "A" }]);
    await queuePendingSerials(kv, "s1", [{ productId: "p1", serial: "B" }]);
    await queuePendingSerials(kv, "s2", [{ productId: "p2", serial: "C" }]);

    expect(await pendingSerials(kv)).toEqual([{ saleId: "s1", items: [{ productId: "p1", serial: "B" }] }, { saleId: "s2", items: [{ productId: "p2", serial: "C" }] }]);
  });

  it("does not call the server while the sale is still in the upload queue, then sends once it has gone", async () => {
    const post = vi.fn(async () => ok(undefined));
    const { deps, outbox, kv } = await setup(post);
    const sale = aSale({ id: newId(1) });
    await queueSale(outbox, sale, new Date());
    await queuePendingSerials(kv, sale.id, [{ productId: "p-bread", serial: "SN1" }]);

    expect(await sendPendingSerials(deps)).toEqual({ sent: 0, waiting: 1, failed: [] });
    expect(post).not.toHaveBeenCalled();

    await outbox.remove(sale.id);
    expect(await sendPendingSerials(deps)).toEqual({ sent: 1, waiting: 0, failed: [] });
    expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/sales/${sale.id}/serials`, { deviceId: DEVICE, staffId: STAFF, items: [{ productId: "p-bread", serial: "SN1" }] });
    expect(await pendingSerials(kv)).toEqual([]);
  });

  it("keeps them when there is no connection or the server does not have the sale yet", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(fail("network.unreachable", "offline"))
      .mockResolvedValueOnce(fail("serial.sale_unknown", "Send the sale to the server first."));
    const { deps, kv } = await setup(post);
    await queuePendingSerials(kv, "s1", [{ productId: "p1", serial: "A" }]);

    expect((await sendPendingSerials(deps)).waiting).toBe(1);
    expect((await sendPendingSerials(deps)).waiting).toBe(1);
    expect(await pendingSerials(kv)).toHaveLength(1);
  });

  it("drops a set the server refuses for good and says why", async () => {
    const post = vi.fn(async () => fail("serial.already_sold", "Serial A has already been sold."));
    const { deps, kv } = await setup(post);
    await queuePendingSerials(kv, "s1", [{ productId: "p1", serial: "A" }]);

    expect(await sendPendingSerials(deps)).toEqual({ sent: 0, waiting: 0, failed: [{ saleId: "s1", message: "Serial A has already been sold." }] });
    expect(await pendingSerials(kv)).toEqual([]);
  });
});
