// @vitest-environment jsdom
import { MemoryOutbox, queueBuilt, queueSale, stockMovementRecord, cashUpRecord, type SaleUploadDto, type SendBatch, type UploadRequest } from "@tillknowit/domain";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { httpSend, loadDeviceConfig, startSync } from "./syncRunner";

const sale = (id: string): SaleUploadDto => ({
  id, staffId: "s", soldAt: "2026-10-26T08:00:00.000Z", paymentMethodCode: "card", totalCents: 100, vatCents: 13, tenderedCents: null, changeCents: null, accountId: null,
  lines: [{ productId: "p", name: "Bread", quantity: 1, priceCents: 100, costCents: 50, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
});

const accepting: SendBatch = async (r) => ({ results: r.sales.map((s) => ({ kind: "sale", id: s.id, status: "accepted" as const })) });
const settle = () => new Promise((r) => setTimeout(r, 0));

beforeEach(() => localStorage.clear());

describe("startSync", () => {
  it("sends what is waiting when there is a connection and a registered device, and reports the counts", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());
    const counts: Array<{ waiting: number; refused: number }> = [];

    const handle = startSync({ outbox, config: () => ({ shopId: "shop", deviceId: "till" }), send: accepting, onCounts: (c) => counts.push(c), isOnline: () => true, setTimer: () => 0, clearTimer: () => {} });
    await settle();

    expect(await outbox.all()).toEqual([]);
    expect(counts.at(-1)).toEqual({ waiting: 0, refused: 0 });
    handle.stop();
  });

  it("keeps the sales and sends nothing when the till is not registered or is offline", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());
    const send = vi.fn(accepting);
    const counts: Array<{ waiting: number; refused: number }> = [];

    const unregistered = startSync({ outbox, config: () => null, send, onCounts: (c) => counts.push(c), isOnline: () => true, setTimer: () => 0, clearTimer: () => {} });
    const offline = startSync({ outbox, config: () => ({ shopId: "s", deviceId: "d" }), send, onCounts: (c) => counts.push(c), isOnline: () => false, setTimer: () => 0, clearTimer: () => {} });
    await settle();

    expect(send).not.toHaveBeenCalled();
    expect(counts.at(-1)).toEqual({ waiting: 1, refused: 0 });
    unregistered.stop();
    offline.stop();
  });

  it("tries again after a failure with a growing wait and keeps the sale until it gets through", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());
    let fail = true;
    const send: SendBatch = async (r) => { if (fail) throw new Error("down"); return accepting(r); };
    const waits: number[] = [];
    let pending: (() => void) | null = null;

    const handle = startSync({ outbox, config: () => ({ shopId: "s", deviceId: "d" }), send, onCounts: () => {}, isOnline: () => true, setTimer: (fn, ms) => { waits.push(ms); pending = fn; return 1; }, clearTimer: () => {} });
    await settle();
    expect(waits).toEqual([5_000]);
    expect((await outbox.all()).length).toBe(1);

    pending!();
    await settle();
    expect(waits).toEqual([5_000, 10_000]);

    fail = false;
    pending!();
    await settle();
    expect(await outbox.all()).toEqual([]);
    handle.stop();
  });

  it("runs one send at a time however often it is nudged and does nothing after it is stopped", async () => {
    const outbox = new MemoryOutbox();
    await queueSale(outbox, sale("a"), new Date());
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    const send = vi.fn<SendBatch>(async (r) => { await gate; return accepting(r); });

    const handle = startSync({ outbox, config: () => ({ shopId: "s", deviceId: "d" }), send, onCounts: () => {}, isOnline: () => true, setTimer: () => 0, clearTimer: () => {} });
    handle.nudge();
    handle.nudge();
    await settle();
    expect(send).toHaveBeenCalledTimes(1);

    release();
    await settle();
    handle.stop();
    await queueSale(outbox, sale("b"), new Date());
    handle.nudge();
    await settle();
    expect(send).toHaveBeenCalledTimes(1);
  });
});

describe("startSync with every kind of record", () => {
  it("sends sales, stock movements and cash-ups together and keeps the one the server refused", async () => {
    const outbox = new MemoryOutbox();
    const at = new Date("2026-10-26T08:00:00Z");
    await queueSale(outbox, sale("a"), at);
    await queueBuilt(outbox, "stock_movement", stockMovementRecord({ productId: "p", type: "receive", quantity: 3 }, { id: "m1", staffId: "s", at }), at);
    await queueBuilt(outbox, "cash_up", cashUpRecord({ floatCents: 0, expectedCents: 0, countedCents: 0 }, { id: "u1", staffId: "s", at }), at);
    const requests: UploadRequest[] = [];
    const send: SendBatch = async (r) => {
      requests.push(r);
      return { results: [{ kind: "sale", id: "a", status: "accepted" }, { kind: "stock_movement", id: "m1", status: "accepted" }, { kind: "cash_up", id: "u1", status: "rejected", code: "cashup.exists", message: "Already done." }] };
    };
    const counts: Array<{ waiting: number; refused: number }> = [];

    const handle = startSync({ outbox, config: () => ({ shopId: "shop", deviceId: "till" }), send, onCounts: (c) => counts.push(c), isOnline: () => true, setTimer: () => 0, clearTimer: () => {} });
    await settle();

    expect(requests).toHaveLength(1);
    expect(requests[0]).toMatchObject({ shopId: "shop", deviceId: "till", sales: [{ id: "a" }], stockMovements: [{ id: "m1" }], cashUps: [{ id: "u1" }] });
    expect((await outbox.all()).map((e) => [e.id, e.state])).toEqual([["u1", "rejected"]]);
    expect(counts.at(-1)).toEqual({ waiting: 0, refused: 1 });
    handle.stop();
  });

  it("posts the other lists in the same json body as the sales", async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({ results: [] }), { status: 200 }));
    await httpSend("/api/v1", fetcher as unknown as typeof fetch)({ shopId: "s", deviceId: "d", sales: [], stockMovements: [{ id: "m" }] });

    expect(JSON.parse((fetcher.mock.calls[0] as unknown as [string, { body: string }])[1].body)).toEqual({ shopId: "s", deviceId: "d", sales: [], stockMovements: [{ id: "m" }] });
  });
});

describe("device config and the http send", () => {
  it("reads the registered device and ignores anything malformed", () => {
    expect(loadDeviceConfig()).toBeNull();
    localStorage.setItem("tillknowit.device", "nope");
    expect(loadDeviceConfig()).toBeNull();
    localStorage.setItem("tillknowit.device", JSON.stringify({ shopId: 1 }));
    expect(loadDeviceConfig()).toBeNull();
    localStorage.setItem("tillknowit.device", JSON.stringify({ shopId: "s", deviceId: "d" }));
    expect(loadDeviceConfig()).toEqual({ shopId: "s", deviceId: "d" });
  });

  it("posts the batch as json and treats any failure status as an error", async () => {
    const ok = vi.fn(async () => new Response(JSON.stringify({ results: [] }), { status: 200 }));
    await httpSend("/api/v1", ok as unknown as typeof fetch)({ shopId: "s", deviceId: "d", sales: [sale("a")] });
    expect(ok).toHaveBeenCalledWith("/api/v1/sync/upload", expect.objectContaining({ method: "POST" }));
    expect(JSON.parse((ok.mock.calls[0] as unknown as [string, { body: string }])[1].body).sales[0].id).toBe("a");

    const bad = (async () => new Response("no", { status: 413 })) as unknown as typeof fetch;
    await expect(httpSend("/api/v1", bad)({ shopId: "s", deviceId: "d", sales: [] })).rejects.toThrow("413");
  });

  it("sends the stored device token with the upload, read at the time of each send", async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({ results: [] }), { status: 200 }));
    const send = httpSend("/api/v1", fetcher as unknown as typeof fetch);
    const headers = (n: number) => (fetcher.mock.calls[n] as unknown as [string, { headers: Record<string, string> }])[1].headers;

    await send({ shopId: "s", deviceId: "d", sales: [] });
    expect(headers(0)).toEqual({ "Content-Type": "application/json" });
    localStorage.setItem("tillknowit.deviceToken", "tok-9");
    await send({ shopId: "s", deviceId: "d", sales: [] });
    expect(headers(1)).toEqual({ "Content-Type": "application/json", "X-Device-Token": "tok-9" });
  });
});
