// @vitest-environment jsdom
import { isRunning } from "@tillknowit/domain";
import { beforeEach, describe, expect, it } from "vitest";
import { applyProductPull, loadCatalogue, mapDays, mapPromotions, productsAsOf, type PulledProduct, type ServerPromotion } from "./catalogueStore";
import { MemoryKeyValueStore } from "./localData";
import { chooseIncomingPack, loadPack, mapPack, savePack } from "./packStore";
import { checkRegistration, clearDeviceConfig, saveDeviceConfig } from "./registration";
import { loadStaff, mapStaff, saveStaff } from "./staffStore";
import { loadDeviceConfig } from "./syncRunner";

beforeEach(() => localStorage.clear());

const UUID = "0192a3b4-0000-7000-8000-00000000b001";

describe("staff from the server", () => {
  it("keeps the membership id, drops disabled and PIN-less people and unknown roles, and ignores the case of the role", () => {
    const staff = mapStaff([
      { staffId: "m1", name: "Olive", role: "Owner", pinHash: "h", disabled: false },
      { staffId: "m2", name: "Off", role: "cashier", pinHash: "h", disabled: true },
      { staffId: "m3", name: "None", role: "cashier", pinHash: null, disabled: false },
      { staffId: "m4", name: "Empty", role: "cashier", pinHash: "", disabled: false },
      { staffId: "m5", name: "Odd", role: "auditor", pinHash: "h", disabled: false },
    ]);

    expect(staff).toEqual([{ id: "m1", name: "Olive", role: "owner", pinHash: "h" }]);
  });

  it("gives a registered till only its downloaded people, and a dev build only an unregistered till the examples", async () => {
    const kv = new MemoryKeyValueStore();

    expect((await loadStaff(kv)).map((s) => s.name)).toEqual(["Thandi", "Sipho"]);   // dev build, not registered
    saveDeviceConfig({ shopId: UUID, deviceId: UUID });
    expect(await loadStaff(kv)).toEqual([]);
    await saveStaff(kv, [{ id: "m1", name: "Olive", role: "owner", pinHash: "h" }]);
    expect((await loadStaff(kv)).map((s) => s.name)).toEqual(["Olive"]);
    clearDeviceConfig();
    expect((await loadStaff(kv)).map((s) => s.name)).toEqual(["Olive"]);   // downloaded people win over the examples
  });
});

describe("products from the server", () => {
  const p = (id: string, over: Partial<PulledProduct> = {}): PulledProduct => ({
    id, name: id, barcode: " 6001 ", categoryCode: "x", costCents: 1, priceCents: 2, lowStockLevel: 3, expiryDate: "2026-12-01", isActive: true, soldByWeight: true,
    isAddOn: false, requiresSerial: false, parentProductId: null, variantLabel: null, updatedAt: "2026-10-26T09:00:00Z", ...over,
  });

  it("maps to what the till sells with and keeps what the stock screens need, sorted by name", async () => {
    const kv = new MemoryKeyValueStore();
    await applyProductPull(kv, { products: [p("zucchini"), p("apple", { barcode: "" })], asOf: "2026-10-26T09:59:00Z" });

    const catalogue = await loadCatalogue(kv);

    expect(catalogue.map((c) => c.id)).toEqual(["apple", "zucchini"]);
    expect(catalogue[0]).toMatchObject({ barcode: null, soldByWeight: true, lowStockLevel: 3, expiryDate: "2026-12-01", categoryCode: "x" });
    expect(catalogue[1]!.barcode).toBe("6001");
    expect(await productsAsOf(kv)).toBe("2026-10-26T09:59:00Z");
  });

  it("gives a development build examples only while the till is not registered and has downloaded nothing", async () => {
    const kv = new MemoryKeyValueStore();
    expect((await loadCatalogue(kv)).map((c) => c.id)).toContain("dev-bread");

    saveDeviceConfig({ shopId: UUID, deviceId: UUID });
    expect(await loadCatalogue(kv)).toEqual([]);
  });
});

describe("promotions from the server", () => {
  const base: ServerPromotion = { id: "p", name: "Deal", type: "percent_off", productIds: null, percent: 10, amountCents: 0, items: null, startsAt: "2026-10-01T00:00:00Z", endsAt: "2026-12-01T00:00:00Z", days: null, from: null, to: null, isActive: true };

  it("reads day names in any case or length, and every day when none are named", () => {
    expect(mapDays(["Mon", "TUE", "wednesday", "Sun", "Mon"])).toEqual([0, 1, 2, 3]);
    expect(mapDays([])).toEqual([0, 1, 2, 3, 4, 5, 6]);
    expect(mapDays(null)).toEqual([0, 1, 2, 3, 4, 5, 6]);
    expect(mapDays(["Funday"])).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  it("maps to the domain's promotion so it runs when the server says it should, in South African time", () => {
    const [promo] = mapPromotions([{ ...base, days: ["Mon"], from: "16:00:00", to: "18:00" }]);

    expect(promo).toMatchObject({ days: [1], from: "16:00", to: "18:00", productIds: [], items: [] });
    expect(isRunning(promo!, new Date("2026-10-26T14:30:00Z"))).toBe(true);    // Monday 16:30 in Johannesburg
    expect(isRunning(promo!, new Date("2026-10-27T14:30:00Z"))).toBe(false);   // Tuesday
    expect(isRunning(promo!, new Date("2026-10-26T10:30:00Z"))).toBe(false);   // Monday 12:30
  });

  it("carries combos and leaves out a kind it does not know", () => {
    const promos = mapPromotions([{ ...base, id: "c", type: "combo", items: [{ productId: "a", quantity: 1 }, { productId: "b", quantity: 2 }], amountCents: 5000 }, { ...base, id: "u", type: "buy_one_get_one" }]);

    expect(promos.map((x) => x.id)).toEqual(["c"]);
    expect(promos[0]).toMatchObject({ type: "combo", amountCents: 5000, items: [{ productId: "a", quantity: 1 }, { productId: "b", quantity: 2 }] });
  });

  it("keeps an inactive promotion inactive", () => {
    expect(mapPromotions([{ ...base, isActive: false }])[0]!.isActive).toBe(false);
  });
});

describe("the business pack", () => {
  it("maps the server's features and settings and keeps what was downloaded", () => {
    const pack = mapPack({ shopId: UUID, packCode: "cafe", packName: "Cafe", packVersion: 2, slipTemplateCode: "slip.sale", features: [{ flag: "tips" }, { flag: "printer", settingsJson: "{\"a\":1}" }] });
    savePack(pack);

    expect(pack.features).toEqual([{ flag: "tips", settingsJson: null }, { flag: "printer", settingsJson: "{\"a\":1}" }]);
    expect(loadPack(UUID)).toEqual(pack);
    expect(loadPack("another-shop").packVersion).toBe(0);
  });

  it("takes a new type at once, the higher version of the same type, and never another shop's pack", () => {
    const current = mapPack({ shopId: UUID, packCode: "cafe", packName: "Cafe", packVersion: 5, slipTemplateCode: "s", features: [] });

    expect(chooseIncomingPack(current, { ...current, packCode: "salon", packVersion: 1 }).packCode).toBe("salon");
    expect(chooseIncomingPack(current, { ...current, packVersion: 4 })).toBe(current);
    expect(chooseIncomingPack(current, { ...current, packVersion: 6 }).packVersion).toBe(6);
    expect(chooseIncomingPack(current, { ...current, shopId: "other", packCode: "salon" })).toBe(current);
  });
});

describe("the shop name on the pack", () => {
  const base = { shopId: UUID, packCode: "cafe", packName: "Cafe", packVersion: 2, slipTemplateCode: "s", features: [] };

  it("keeps the shop name the server sends and leaves it out when there is none", () => {
    expect(mapPack({ ...base, shopName: "Mama Thandi" }).shopName).toBe("Mama Thandi");
    expect("shopName" in mapPack({ ...base, shopName: "" })).toBe(false);
  });

  it("takes the name onto a pack of the same version that was saved without one", () => {
    const old = mapPack(base);
    const chosen = chooseIncomingPack(old, mapPack({ ...base, shopName: "Mama Thandi" }));

    expect(chosen.shopName).toBe("Mama Thandi");
    expect(chosen.packVersion).toBe(2);
    expect(chooseIncomingPack(chosen, mapPack({ ...base, shopName: "Mama Thandi" }))).toBe(chosen);
  });
});

describe("registration", () => {
  it("accepts the two ids as typed, trimmed and in lower case, and says which one is wrong", () => {
    expect(checkRegistration(` ${UUID.toUpperCase()} `, UUID)).toEqual({ ok: true, device: { shopId: UUID, deviceId: UUID } });
    expect(checkRegistration("nonsense", UUID)).toEqual({ ok: false, error: "shop" });
    expect(checkRegistration(UUID, "")).toEqual({ ok: false, error: "device" });
  });

  it("stores the device where the sync reads it and can clear it", () => {
    expect(saveDeviceConfig({ shopId: UUID, deviceId: UUID })).toBe(true);
    expect(JSON.parse(localStorage.getItem("tillknowit.device")!)).toEqual({ shopId: UUID, deviceId: UUID });
    expect(loadDeviceConfig()).toEqual({ shopId: UUID, deviceId: UUID });
    clearDeviceConfig();
    expect(loadDeviceConfig()).toBeNull();
  });
});
