import { describe, expect, it } from "vitest";
import {
  buildEmbeddedBarcode, checkDigit, hasValidCheckDigit, isRunning, lineGross, lineTotal, parseBarcodeRules, parseEmbeddedBarcode, priceBasket, priceModifiers, weighedLineTotal,
  type EmbeddedBarcodeRules, type ModifierGroup, type PromotionDef,
} from "./selling";

const unwrap = <T>(r: { ok: true; value: T } | { ok: false; code: string }): T => {
  if (!r.ok) throw new Error(r.code);
  return r.value;
};

describe("line maths", () => {
  it("is price times quantity for ordinary items and rounds half up for grams", () => {
    expect(lineGross(1500, 3)).toBe(4500);
    expect(lineGross(8999, 250, 1000)).toBe(2250);   // 2249.75 rounds up
    expect(lineGross(1, 500, 1000)).toBe(1);          // 0.5 rounds up
    expect(lineTotal(1500, 3, 1, 500)).toBe(4000);
  });
});

describe("weighed items", () => {
  it("prices the net weight at the price per kilogram, half up", () => {
    expect(unwrap(weighedLineTotal(12_000, 1_000))).toBe(12_000);
    expect(unwrap(weighedLineTotal(8_999, 255, 5))).toBe(2_250);   // 250 g net
    expect(unwrap(weighedLineTotal(1, 500))).toBe(1);
  });

  it("refuses silly weights and tares with the server's codes", () => {
    expect(weighedLineTotal(100, 0)).toMatchObject({ ok: false, code: "weight.weight_invalid" });
    expect(weighedLineTotal(100, 50_001)).toMatchObject({ ok: false, code: "weight.weight_invalid" });
    expect(weighedLineTotal(100, 500, 500)).toMatchObject({ ok: false, code: "weight.tare_invalid" });
    expect(weighedLineTotal(-1, 500)).toMatchObject({ ok: false, code: "weight.price_invalid" });
  });
});

describe("price-embedded barcodes", () => {
  // The butchery pack: prefix 2, six item digits, five value digits, weight with three decimals.
  const butchery: EmbeddedBarcodeRules = { prefix: "2", itemDigits: 6, valueDigits: 5, valueType: "weight", decimals: 3 };

  it("has the EAN-13 check digit of the standard example", () => {
    expect(checkDigit("400638133393")).toBe(1);   // 4006381333931
    expect(hasValidCheckDigit("4006381333931")).toBe(true);
    expect(hasValidCheckDigit("4006381333932")).toBe(false);
    expect(hasValidCheckDigit("40063813339")).toBe(false);
  });

  it("reads an item and a weight in grams off a label and reads back what it built", () => {
    const code = buildEmbeddedBarcode(butchery, "123456", 1_250);
    expect(code).toHaveLength(13);
    expect(parseEmbeddedBarcode(code, butchery)).toEqual({ itemCode: "123456", weightGrams: 1_250, priceCents: null });
  });

  it("leaves ordinary barcodes, bad check digits and empty values alone", () => {
    expect(parseEmbeddedBarcode("6001234567890", butchery)).toBeNull();               // another prefix
    const code = buildEmbeddedBarcode(butchery, "123456", 1_250);
    expect(parseEmbeddedBarcode(code.slice(0, 12) + ((Number(code[12]) + 1) % 10), butchery)).toBeNull();
    expect(parseEmbeddedBarcode(buildEmbeddedBarcode(butchery, "123456", 0), butchery)).toBeNull();
    expect(parseEmbeddedBarcode(null, butchery)).toBeNull();
  });

  it("reads prices with the number of decimals the rules say", () => {
    const price: EmbeddedBarcodeRules = { prefix: "20", itemDigits: 5, valueDigits: 5, valueType: "price", decimals: 2 };
    expect(parseEmbeddedBarcode(buildEmbeddedBarcode(price, "00042", 4_599), price)).toEqual({ itemCode: "00042", weightGrams: null, priceCents: 4_599 });
    const whole: EmbeddedBarcodeRules = { ...price, decimals: 0 };
    expect(parseEmbeddedBarcode(buildEmbeddedBarcode(whole, "00042", 46), whole)?.priceCents).toBe(4_600);
  });

  it("reads the pack setting as the server stores it", () => {
    const json = "{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\",\"decimals\":3}";
    expect(unwrap(parseBarcodeRules(json))).toEqual(butchery);
    expect(parseBarcodeRules(null)).toMatchObject({ ok: false, code: "barcode.rules_missing" });
    expect(parseBarcodeRules("nope")).toMatchObject({ ok: false, code: "barcode.rules_invalid" });
    expect(parseBarcodeRules("{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":9}")).toMatchObject({ ok: false, code: "barcode.rules_invalid" });
  });
});

describe("modifiers", () => {
  const sauce: ModifierGroup = { id: "g1", name: "Sauce", required: true, min: 1, max: 2, options: [{ id: "a", name: "Mayo", priceCents: 0 }, { id: "b", name: "Peri-peri", priceCents: 300 }, { id: "c", name: "Cheese", priceCents: 500 }] };
  const extras: ModifierGroup = { id: "g2", name: "Extras", required: false, min: 0, max: 3, options: [{ id: "d", name: "Egg", priceCents: 700 }] };

  it("adds up the chosen options and checks every group", () => {
    expect(unwrap(priceModifiers([sauce, extras], ["b", "d"]))).toBe(1_000);
    expect(priceModifiers([sauce, extras], ["d"])).toMatchObject({ ok: false, code: "modifier.too_few" });       // a required group left empty
    expect(priceModifiers([sauce], ["a", "b", "c"])).toMatchObject({ ok: false, code: "modifier.too_many" });
    expect(priceModifiers([sauce], ["a", "a"])).toMatchObject({ ok: false, code: "modifier.repeated" });
    expect(priceModifiers([sauce], ["zzz"])).toMatchObject({ ok: false, code: "modifier.unknown" });
  });
});

describe("promotions", () => {
  const coke = "00000000-0000-0000-0000-0000000000c1";
  const chips = "00000000-0000-0000-0000-0000000000c2";
  // Monday 26 October 2026, 10:00 in South Africa.
  const monday = new Date("2026-10-26T08:00:00Z");
  const promo = (over: Partial<PromotionDef>): PromotionDef => ({
    id: "00000000-0000-0000-0000-000000000001", name: "P", type: "percent_off", productIds: [], percent: 0, amountCents: 0, items: [],
    startsAt: new Date("2026-10-01T00:00:00Z"), endsAt: new Date("2026-12-01T00:00:00Z"), days: [0, 1, 2, 3, 4, 5, 6], ...over,
  });

  it("runs on the days and hours it is set for, in South African time", () => {
    const happyHour = promo({ days: [1], from: "16:00", to: "18:00" });
    expect(isRunning(happyHour, new Date("2026-10-26T14:30:00Z"))).toBe(true);    // 16:30 on Monday
    expect(isRunning(happyHour, new Date("2026-10-26T08:00:00Z"))).toBe(false);   // 10:00
    expect(isRunning(happyHour, new Date("2026-10-27T14:30:00Z"))).toBe(false);   // Tuesday
    const late = promo({ from: "22:00", to: "02:00" });
    expect(isRunning(late, new Date("2026-10-26T21:30:00Z"))).toBe(true);         // 23:30
    expect(isRunning(late, new Date("2026-10-26T23:30:00Z"))).toBe(true);         // 01:30 the next morning
    expect(isRunning(late, new Date("2026-10-26T12:00:00Z"))).toBe(false);
    expect(isRunning(promo({ isActive: false }), monday)).toBe(false);
    expect(isRunning(promo({ endsAt: monday }), monday)).toBe(false);             // the end is exclusive
  });

  it("returns the basket unchanged when nothing is running", () => {
    const priced = priceBasket([{ productId: coke, quantity: 2, unitPriceCents: 1_500 }], [], monday);
    expect(priced).toMatchObject({ subtotalCents: 3_000, discountCents: 0, totalCents: 3_000 });
  });

  it("takes the best per-unit promotion and promotions do not stack", () => {
    const tenPercent = promo({ id: "00000000-0000-0000-0000-000000000001", name: "10% off", percent: 10 });
    const twoOff = promo({ id: "00000000-0000-0000-0000-000000000002", name: "R2 off Coke", type: "amount_off", productIds: [coke], amountCents: 200 });

    const priced = priceBasket([{ productId: coke, quantity: 3, unitPriceCents: 1_500 }, { productId: chips, quantity: 1, unitPriceCents: 1_000 }], [tenPercent, twoOff], monday);

    expect(priced.discountsByProduct.get(coke)).toBe(3 * 200);   // R2 beats 10% of R15 = R1.50
    expect(priced.discountsByProduct.get(chips)).toBe(100);
    expect(priced.totalCents).toBe(5_500 - 700);
    expect(priced.applied.map((a) => [a.name, a.times])).toEqual([["R2 off Coke", 3], ["10% off", 1]]);
  });

  it("fits a combo first and the saving adds up to exactly what the combo saves", () => {
    const combo = promo({ id: "00000000-0000-0000-0000-000000000009", name: "Lunch", type: "combo", amountCents: 2_000, items: [{ productId: coke, quantity: 1 }, { productId: chips, quantity: 1 }] });
    const tenPercent = promo({ id: "00000000-0000-0000-0000-000000000001", name: "10% off", percent: 10 });

    const priced = priceBasket([{ productId: coke, quantity: 2, unitPriceCents: 1_500 }, { productId: chips, quantity: 1, unitPriceCents: 1_000 }], [combo, tenPercent], monday);

    // One combo (R25 for R20, saving R5), then the second Coke gets 10%.
    expect(priced.discountsByProduct.get(coke)! + priced.discountsByProduct.get(chips)!).toBe(500 + 150);
    expect(priced.totalCents).toBe(4_000 - 650);
    expect(priced.applied.find((a) => a.name === "Lunch")).toMatchObject({ discountCents: 500, times: 1 });
  });

  it("never takes more off than a unit costs", () => {
    const big = promo({ type: "amount_off", productIds: [coke], amountCents: 99_999 });
    expect(priceBasket([{ productId: coke, quantity: 2, unitPriceCents: 1_500 }], [big], monday).totalCents).toBe(0);
  });
});
