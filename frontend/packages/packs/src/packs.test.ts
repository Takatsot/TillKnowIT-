import { describe, expect, it } from "vitest";
import { Features, defaultPack, featureFlags, newerPack, type ShopPack } from "./index";

const pack = (flags: Array<string | [string, string]>, version = 3): ShopPack => ({
  shopId: "shop-1", packCode: "butchery", packName: "Butchery", packVersion: version, slipTemplateCode: "slip.sale",
  features: flags.map((f) => (typeof f === "string" ? { flag: f } : { flag: f[0], settingsJson: f[1] })),
});

describe("features", () => {
  it("knows the flags the server knows", () => {
    expect(featureFlags).toHaveLength(23);
    expect(new Set(featureFlags).size).toBe(23);
  });

  it("is on only for the features in the pack, and the default pack has none", () => {
    const f = new Features(pack(["weight", "scale"]));

    expect(f.isOn("weight")).toBe(true);
    expect(f.isOn("tables")).toBe(false);
    expect(new Features(defaultPack("s")).isOn("weight")).toBe(false);
    expect(new Features(defaultPack("s")).paymentMethods()).toEqual(["cash", "card", "qr", "credit"]);
  });

  it("gives a feature's settings only while it is on", () => {
    const json = "{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\",\"decimals\":3}";
    const f = new Features(pack([["price_embedded_barcodes", json], "weight"]));

    expect(f.settings("price_embedded_barcodes")).toBe(json);
    expect(f.settings("weight")).toBeNull();
    expect(f.barcodeRules()).toMatchObject({ ok: true, value: { prefix: "2", itemDigits: 6, valueDigits: 5, valueType: "weight", decimals: 3 } });
    expect(new Features(pack(["weight"])).barcodeRules()).toMatchObject({ ok: false, code: "barcode.rules_missing" });
  });

  it("adds split and lay-by to the payment methods only when the pack has them", () => {
    expect(new Features(pack(["split_payments"])).paymentMethods()).toEqual(["cash", "card", "qr", "credit", "split"]);
    expect(new Features(pack(["split_payments", "layby"])).paymentMethods()).toEqual(["cash", "card", "qr", "credit", "split", "layby"]);
  });

  it("asks for hardware only where the business can use it", () => {
    const f = new Features(pack(["printer", "kitchen"]));

    expect([f.needs("printer"), f.needs("kitchen"), f.needs("scale"), f.needs("cash_drawer")]).toEqual([true, true, false, false]);
  });

  it("ignores a flag from a newer server rather than failing, and reports it", () => {
    const f = new Features(pack(["weight", "hologram_receipts"]));

    expect(f.isOn("weight")).toBe(true);
    expect(f.unknownFlags()).toEqual(["hologram_receipts"]);
  });
});

describe("keeping the newest pack", () => {
  it("takes a higher version of the same shop's pack and keeps the current one otherwise", () => {
    const current = pack(["weight"], 3);

    expect(newerPack(current, pack(["weight", "scale"], 4)).packVersion).toBe(4);
    expect(newerPack(current, pack(["scale"], 3))).toBe(current);
    expect(newerPack(current, pack(["scale"], 2))).toBe(current);
    expect(newerPack(current, { ...pack(["scale"], 9), shopId: "other-shop" })).toBe(current);
  });
});
