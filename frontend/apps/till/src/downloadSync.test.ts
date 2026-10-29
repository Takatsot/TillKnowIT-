// @vitest-environment jsdom
import type { Api } from "@tillknowit/api-client";
import { fail, MemoryOutbox, ok, queueSale, type Result, type SaleUploadDto } from "@tillknowit/domain";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { loadCatalogue, loadPromotions, type PulledProduct, type ServerPromotion } from "./catalogueStore";
import { loadDeviceToken, loadEnrolmentCode, saveDeviceToken, saveEnrolmentCode } from "./deviceToken";
import { loadStatus, pullAll, startDownloadSync, type PullDeps, type PullResult } from "./downloadSync";
import { MemoryKeyValueStore } from "./localData";
import { loadPack } from "./packStore";
import { saveDeviceConfig } from "./registration";
import { loadStaff, type ServerStaff } from "./staffStore";
import { loadDeviceConfig } from "./syncRunner";

const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
const device = { shopId: SHOP, deviceId: DEVICE };
const now = new Date("2026-10-26T10:00:00Z");

const staffRows: ServerStaff[] = [
  { staffId: "m-owner", name: "Olive", role: "owner", pinHash: "h1", disabled: false },
  { staffId: "m-cashier", name: "Thandi", role: "cashier", pinHash: "h2", disabled: false },
  { staffId: "m-gone", name: "Gone", role: "cashier", pinHash: "h3", disabled: true },
  { staffId: "m-nopin", name: "NoPin", role: "cashier", pinHash: null, disabled: false },
];
const product = (id: string, over: Partial<PulledProduct> = {}): PulledProduct => ({
  id, name: id, barcode: null, categoryCode: "general", costCents: 100, priceCents: 200, lowStockLevel: 0, expiryDate: null, isActive: true, soldByWeight: false,
  isAddOn: false, requiresSerial: false, parentProductId: null, variantLabel: null, updatedAt: "2026-10-26T09:00:00Z", ...over,
});
const promotion: ServerPromotion = {
  id: "promo-1", name: "Weekend", type: "percent_off", productIds: ["bread"], percent: 10, amountCents: 0, items: [], startsAt: "2026-10-01T00:00:00Z", endsAt: "2026-12-01T00:00:00Z",
  days: ["Sat", "Sun"], from: "08:00:00", to: "17:00:00", isActive: true,
};
const packRow = { shopId: SHOP, packCode: "butchery", packName: "Butchery", packVersion: 3, slipTemplateCode: "slip.sale", features: [{ flag: "weight", settingsJson: null }, { flag: "tips" }] };

type Routes = Record<string, (query?: Record<string, unknown>, body?: unknown) => Result<unknown>>;

function fakeApi(routes: Routes) {
  const calls: string[] = [];
  const handle = (method: string) => async (path: string, a?: unknown, b?: unknown) => {
    const key = `${method} ${path}`;
    calls.push(key);
    const route = routes[key];
    return route ? route((method === "GET" ? a : b) as never, method === "GET" ? undefined : a) : fail("http.404", `no route ${key}`);
  };
  const api = { get: vi.fn(handle("GET")), post: vi.fn(handle("POST")) } as unknown as Api;
  return { api, calls };
}

const healthy = (over: Routes = {}): Routes => ({
  [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: false }),
  [`GET /shops/${SHOP}/devices/${DEVICE}/staff`]: () => ok(staffRows),
  [`GET /shops/${SHOP}/sync/products`]: () => ok({ products: [product("bread", { barcode: "6001", priceCents: 1800, costCents: 1200 }), product("old", { isActive: false })], asOf: "2026-10-26T09:59:00Z" }),
  [`GET /shops/${SHOP}/promotions`]: () => ok([promotion]),
  [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => ok(packRow),
  ...over,
});

function deps(routes: Routes, extra: Partial<PullDeps> = {}) {
  const kv = new MemoryKeyValueStore();
  const outbox = new MemoryOutbox();
  const { api, calls } = fakeApi(routes);
  const all: PullDeps = { api, device, kv, outbox, now: () => now, ...extra };
  return { all, kv, outbox, calls, api };
}

const sale = (id: string): SaleUploadDto => ({
  id, staffId: "m-cashier", soldAt: "2026-10-26T08:00:00.000Z", paymentMethodCode: "card", totalCents: 100, vatCents: 13, tenderedCents: null, changeCents: null, accountId: null,
  lines: [{ productId: "p", name: "Bread", quantity: 1, priceCents: 100, costCents: 50, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
});

beforeEach(() => localStorage.clear());

describe("the till credential", () => {
  const checkIn = `POST /devices/${DEVICE}/check-in`;
  const firstBody = (api: Api) => (api.post as unknown as { mock: { calls: unknown[][] } }).mock.calls[0]![1];

  it("gives the setup code on the first check-in, keeps the token it gets back and forgets the code", async () => {
    saveEnrolmentCode("ABCD-1234");
    const { all, api } = deps(healthy({ [checkIn]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: false, token: "tok-1" }) }));

    await pullAll(all);

    expect(firstBody(api)).toEqual({ enrolmentCode: "ABCD-1234" });
    expect(loadDeviceToken()).toBe("tok-1");
    expect(loadEnrolmentCode()).toBeNull();
  });

  it("sends no code once it has a token, and replaces the token with the fresh one every check-in", async () => {
    saveDeviceToken("tok-1");
    saveEnrolmentCode("STALE");
    const { all, api } = deps(healthy({ [checkIn]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: false, token: "tok-2" }) }));

    await pullAll(all);

    expect(firstBody(api)).toBeUndefined();
    expect(loadDeviceToken()).toBe("tok-2");
  });

  it.each([["device.enrolment_required", "code"], ["device.credential_required", "again"], ["auth.device_required", "again"]] as const)(
    "%s means the till must be set up again, downloads nothing and does not touch the sales waiting",
    async (code, expected) => {
      const { all, outbox, calls, kv } = deps(healthy({ [checkIn]: () => fail(code, "Refused.") }));
      await queueSale(outbox, sale("waiting"), now);

      const result = await pullAll(all);

      expect(result.status.credential).toBe(expected);
      expect(result.retry).toBe("later");
      expect(calls).toEqual([checkIn]);
      expect((await outbox.all()).length).toBe(1);
      expect((await loadStatus(kv)).credential).toBe(expected);
    },
  );

  it("treats auth.device_required on a later call the same way", async () => {
    const { all, outbox } = deps(healthy({ [`GET /shops/${SHOP}/devices/${DEVICE}/staff`]: () => fail("auth.device_required", "Set it up again.") }));
    await queueSale(outbox, sale("waiting"), now);

    const result = await pullAll(all);

    expect(result.status.credential).toBe("again");
    expect((await outbox.all()).length).toBe(1);
  });

  it("is back to ok after a good check-in", async () => {
    const first = deps(healthy({ [checkIn]: () => fail("device.credential_required", "Refused.") }));
    await pullAll(first.all);
    const second = deps(healthy(), { kv: first.kv });

    expect((await pullAll(second.all)).status.credential).toBe("ok");
  });
});

describe("pullAll", () => {
  it("downloads staff, products, promotions and the pack, and records when it last worked", async () => {
    const { all, kv, calls } = deps(healthy());

    const result = await pullAll(all);

    expect(result.retry).toBe("later");
    expect(result.changed).toEqual({ staff: true, products: true, promotions: true, pack: true });
    expect(result.status).toMatchObject({ deviceStatus: "approved", lastSyncAt: now.toISOString(), lastError: null, wipe: "none" });
    expect(calls).toEqual([
      `POST /devices/${DEVICE}/check-in`, `GET /shops/${SHOP}/devices/${DEVICE}/staff`, `GET /shops/${SHOP}/sync/products`, `GET /shops/${SHOP}/promotions`, `GET /shops/${SHOP}/devices/${DEVICE}/business-pack`,
    ]);
    // Staff ids are the server's membership ids; the disabled and the PIN-less are not there to sign in.
    expect((await loadStaff(kv)).map((s) => [s.id, s.name, s.role])).toEqual([["m-owner", "Olive", "owner"], ["m-cashier", "Thandi", "cashier"]]);
    expect(await loadCatalogue(kv)).toEqual([expect.objectContaining({ id: "bread", priceCents: 1800, costCents: 1200, barcode: "6001", soldByWeight: false })]);
    expect(await loadPromotions(kv)).toEqual([expect.objectContaining({ id: "promo-1", days: [0, 6], from: "08:00", to: "17:00", startsAt: new Date("2026-10-01T00:00:00Z") })]);
    expect(loadPack(SHOP)).toMatchObject({ packCode: "butchery", packVersion: 3, features: [{ flag: "weight", settingsJson: null }, { flag: "tips", settingsJson: null }] });
    expect((await loadStatus(kv)).lastSyncAt).toBe(now.toISOString());
  });

  it("asks for products only since the last download and names the till and a staff member in the calls that need them", async () => {
    const seen: Array<Record<string, unknown> | undefined> = [];
    const routes = healthy({
      [`GET /shops/${SHOP}/sync/products`]: (q) => { seen.push(q); return ok({ products: [], asOf: "2026-10-26T09:59:00Z" }); },
      [`GET /shops/${SHOP}/promotions`]: (q) => { seen.push(q); return ok([]); },
      [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: (q) => { seen.push(q); return ok(packRow); },
    });
    const { all } = deps(routes, { staffId: () => "m-cashier" });

    await pullAll(all);
    await pullAll(all);

    expect(seen[0]).toEqual({ deviceId: DEVICE, since: undefined });
    expect(seen[1]).toEqual({ deviceId: DEVICE, staffId: "m-cashier" });
    expect(seen[2]).toBeUndefined();   // the pack is asked for by the device alone
    expect(seen[3]).toEqual({ deviceId: DEVICE, since: "2026-10-26T09:59:00Z" });
  });

  it("falls back to the most senior person downloaded when nobody is signed in", async () => {
    const seen: Array<Record<string, unknown> | undefined> = [];
    const { all } = deps(healthy({ [`GET /shops/${SHOP}/promotions`]: (q) => { seen.push(q); return ok([]); } }));

    await pullAll(all);

    expect(seen[0]).toEqual({ deviceId: DEVICE, staffId: "m-owner" });
  });

  it("applies changes on top of what is there and stops selling deactivated products", async () => {
    const { all, kv } = deps(healthy());
    await pullAll(all);
    const second = deps(healthy({
      [`GET /shops/${SHOP}/sync/products`]: () => ok({ products: [product("bread", { priceCents: 2000, name: "Bread loaf" }), product("milk", { priceCents: 2200 })], asOf: "2026-10-26T10:30:00Z" }),
    }), { kv });
    await pullAll(second.all);
    const third = deps(healthy({ [`GET /shops/${SHOP}/sync/products`]: () => ok({ products: [product("milk", { isActive: false })], asOf: "2026-10-26T11:00:00Z" }) }), { kv });
    await pullAll(third.all);

    expect(await loadCatalogue(kv)).toEqual([expect.objectContaining({ id: "bread", name: "Bread loaf", priceCents: 2000 })]);
  });

  it("shows a pending or disabled till and downloads nothing", async () => {
    for (const status of ["pending", "disabled"] as const) {
      const { all, calls, kv } = deps(healthy({ [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status, wipeRequested: false }) }));

      const result = await pullAll(all);

      expect(result.status.deviceStatus).toBe(status);
      expect(result.retry).toBe(status === "pending" ? "soon" : "later");
      expect(calls).toEqual([`POST /devices/${DEVICE}/check-in`]);
      expect((await loadStatus(kv)).deviceStatus).toBe(status);
      expect(await kv.get("staff")).toBeUndefined();
    }
  });

  it("backs off when the network is down and keeps what it had", async () => {
    const first = deps(healthy());
    await pullAll(first.all);
    const down = deps({ [`POST /devices/${DEVICE}/check-in`]: () => fail("network.unreachable", "We cannot reach the server.") }, { kv: first.kv });

    const result = await pullAll(down.all);

    expect(result.retry).toBe("backoff");
    expect(result.status.lastError).toBe("We cannot reach the server.");
    expect(result.status.lastSyncAt).toBe(now.toISOString());   // the last time it did work
    expect((await loadStaff(first.kv)).length).toBe(2);
  });

  it("backs off when one part fails on the network, and does not call that a successful sync", async () => {
    const first = deps(healthy({ [`GET /shops/${SHOP}/sync/products`]: () => fail("http.503", "The server answered 503.") }));

    const result = await pullAll(first.all);

    expect(result.retry).toBe("backoff");
    expect(result.status.lastSyncAt).toBeNull();
    expect(result.changed.staff).toBe(true);   // what did arrive is kept
  });

  it("treats a refusal of one part as a note, not a failure (for example the pack route needs a person, not a till)", async () => {
    const { all } = deps(healthy({ [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => fail("permission.denied", "You do not have access to this shop.") }));

    const result = await pullAll(all);

    expect(result.retry).toBe("later");
    expect(result.notes).toEqual(["business-pack: permission.denied"]);
    expect(result.status.lastSyncAt).toBe(now.toISOString());
    expect(result.changed.pack).toBe(false);
  });

  it("clears the promotions when the shop's pack does not have them", async () => {
    const first = deps(healthy());
    await pullAll(first.all);
    const off = deps(healthy({ [`GET /shops/${SHOP}/promotions`]: () => fail("feature.not_enabled", "'promotions' is not switched on for this shop.") }), { kv: first.kv });

    await pullAll(off.all);

    expect(await loadPromotions(first.kv)).toEqual([]);
  });

  it("keeps a different business type's pack even though its version is lower, and keeps the newer version of the same type", async () => {
    const first = deps(healthy());
    await pullAll(first.all);
    const older = deps(healthy({ [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => ok({ ...packRow, packVersion: 2 }) }), { kv: first.kv });
    await pullAll(older.all);
    expect(loadPack(SHOP).packVersion).toBe(3);

    const other = deps(healthy({ [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => ok({ ...packRow, packCode: "cafe", packName: "Cafe", packVersion: 1 }) }), { kv: first.kv });
    await pullAll(other.all);
    expect(loadPack(SHOP)).toMatchObject({ packCode: "cafe", packVersion: 1 });
  });
});

describe("wipe request", () => {
  const wiping = (over: Routes = {}) => healthy({ [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: true }), [`POST /devices/${DEVICE}/wipe-confirmed`]: () => ok(undefined), ...over });

  it("clears the downloaded data, the books and the registration, then confirms to the server", async () => {
    saveDeviceConfig(device);
    const first = deps(healthy());
    await pullAll(first.all);
    await first.kv.set("ledger.sales", [{ id: "x" }]);
    const wipe = deps(wiping(), { kv: first.kv });

    const result = await pullAll(wipe.all);

    expect(result.wiped).toBe(true);
    expect(result.status.wipe).toBe("done");
    expect(wipe.calls).toEqual([`POST /devices/${DEVICE}/check-in`, `POST /devices/${DEVICE}/wipe-confirmed`]);
    expect(await first.kv.get("staff")).toBeUndefined();
    expect(await first.kv.get("ledger.sales")).toBeUndefined();
    expect(loadDeviceConfig()).toBeNull();
    expect(loadPack(SHOP).packCode).toBe("spaza");
  });

  it("does not wipe while sales are still waiting: it tries to send them first, and waits if it cannot", async () => {
    saveDeviceConfig(device);
    const stuck = deps(wiping());
    await queueSale(stuck.outbox, sale("a"), now);
    const flush = vi.fn(async () => undefined);

    const result = await pullAll({ ...stuck.all, flush });

    expect(flush).toHaveBeenCalled();
    expect(result.wiped).toBe(false);
    expect(result.retry).toBe("soon");
    expect(result.status).toMatchObject({ wipe: "waiting", waitingSales: 1 });
    expect(stuck.calls).not.toContain(`POST /devices/${DEVICE}/wipe-confirmed`);
    expect((await stuck.outbox.all()).length).toBe(1);
    expect(loadDeviceConfig()).toEqual(device);
  });

  it("wipes once the waiting sales have been sent", async () => {
    saveDeviceConfig(device);
    const d = deps(wiping());
    await queueSale(d.outbox, sale("a"), now);

    const result = await pullAll({ ...d.all, flush: async () => { await d.outbox.remove("a"); } });

    expect(result.wiped).toBe(true);
    expect(loadDeviceConfig()).toBeNull();
  });

  it("confirms again at the next check-in if the confirmation did not get through", async () => {
    saveDeviceConfig(device);
    const d = deps(wiping({ [`POST /devices/${DEVICE}/wipe-confirmed`]: () => fail("network.unreachable", "We cannot reach the server.") }));

    const result = await pullAll(d.all);

    expect(result.wiped).toBe(false);
    expect(result.retry).toBe("soon");
    expect(loadDeviceConfig()).toEqual(device);   // still registered, so the next check-in finds the request again
    expect(await d.kv.get("staff")).toBeUndefined();
  });
});

describe("startDownloadSync", () => {
  const result = (retry: PullResult["retry"], wiped = false): PullResult => ({ status: { deviceStatus: "approved", lastSyncAt: null, lastError: null, wipe: "none", waitingSales: 0, credential: "ok" }, retry, changed: { staff: false, products: false, promotions: false, pack: false }, wiped, notes: [] });
  const settle = () => new Promise((r) => setTimeout(r, 0));

  it("pulls at start, then every interval, and backs off 5, 10 seconds after failures", async () => {
    const waits: number[] = [];
    let timer: (() => void) | null = null;
    const outcomes: PullResult[] = [result("backoff"), result("backoff"), result("later")];
    const run = vi.fn(async () => outcomes.shift() ?? result("later"));
    const seen: PullResult[] = [];

    const handle = startDownloadSync({ run, onResult: (r) => seen.push(r), isOnline: () => true, setTimer: (fn, ms) => { waits.push(ms); timer = fn; return 1; }, clearTimer: () => {} });
    await settle();
    timer!();
    await settle();
    timer!();
    await settle();

    expect(run).toHaveBeenCalledTimes(3);
    expect(waits).toEqual([5_000, 10_000, 300_000]);
    expect(seen).toHaveLength(3);
    handle.stop();
  });

  it("checks soon while waiting for approval, does nothing offline, and stops after a wipe or stop", async () => {
    const waits: number[] = [];
    const run = vi.fn(async () => result("soon"));
    let online = false;
    const handle = startDownloadSync({ run, onResult: () => {}, isOnline: () => online, setTimer: (_fn, ms) => { waits.push(ms); return 1; }, clearTimer: () => {} });
    await settle();
    expect(run).not.toHaveBeenCalled();

    online = true;
    handle.nudge();
    await settle();
    expect(run).toHaveBeenCalledTimes(1);
    expect(waits.at(-1)).toBe(60_000);

    handle.stop();
    handle.nudge();
    await settle();
    expect(run).toHaveBeenCalledTimes(1);

    const wipeRun = vi.fn(async () => result("later", true));
    const w: number[] = [];
    const wiped = startDownloadSync({ run: wipeRun, onResult: () => {}, isOnline: () => true, setTimer: (_f, ms) => { w.push(ms); return 1; }, clearTimer: () => {} });
    await settle();
    expect(w).toEqual([]);
    wiped.stop();
  });

  it("runs one pull at a time however often it is nudged", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    const run = vi.fn(async () => { await gate; return result("later"); });
    const handle = startDownloadSync({ run, onResult: () => {}, isOnline: () => true, setTimer: () => 1, clearTimer: () => {} });

    handle.nudge();
    handle.nudge();
    await settle();
    expect(run).toHaveBeenCalledTimes(1);
    release();
    await settle();
    handle.stop();
  });
});
