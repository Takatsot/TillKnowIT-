import { describe, expect, it } from "vitest";
import {
  addItem, addWeighed, clearPriceOverride, emptyBasket, removeItem, scan, setPriceOverride, setQuantity, setTip, summariseBasket, toSaleUpload,
  type Basket, type CatalogueItem, type Payment,
} from "./basket";
import { buildEmbeddedBarcode, type EmbeddedBarcodeRules, type PromotionDef } from "./selling";

const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
const milk: CatalogueItem = { id: "p-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false };
const mince: CatalogueItem = { id: "p-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true };
const catalogue = [bread, milk, mince];
const rules: EmbeddedBarcodeRules = { prefix: "2", itemDigits: 6, valueDigits: 5, valueType: "weight", decimals: 3 };
const now = new Date("2026-10-26T08:00:00Z");
const ids = { saleId: "sale-1", staffId: "staff-1" };

const must = <T>(r: { ok: true; value: T } | { ok: false; code: string }): T => {
  if (!r.ok) throw new Error(r.code);
  return r.value;
};

describe("building a basket", () => {
  it("adds the same item to the same line", () => {
    let b: Basket = emptyBasket;
    b = must(addItem(b, bread));
    b = must(addItem(b, bread, 2));
    b = must(addItem(b, milk));

    expect(b.entries.map((e) => [e.name, e.quantity])).toEqual([["Bread", 3], ["Milk 1L", 1]]);
  });

  it("refuses silly quantities and a weighed item bought each", () => {
    expect(addItem(emptyBasket, bread, 0)).toMatchObject({ ok: false, code: "basket.quantity_invalid" });
    expect(addItem(emptyBasket, bread, 1.5)).toMatchObject({ ok: false, code: "basket.quantity_invalid" });
    expect(addItem(emptyBasket, bread, 10_000)).toMatchObject({ ok: false, code: "basket.quantity_invalid" });
    expect(addItem(emptyBasket, mince)).toMatchObject({ ok: false, code: "basket.needs_weight" });
    expect(addWeighed(emptyBasket, bread, 500)).toMatchObject({ ok: false, code: "basket.not_by_weight" });
  });

  it("adds a weighed item in grams less the tare and prices it per kilogram", () => {
    const b = must(addWeighed(emptyBasket, mince, 1_255, 5));

    expect(b.entries[0]).toMatchObject({ quantity: 1_250, unitDivisor: 1000 });
    expect(summariseBasket(b, [], now).totalCents).toBe(15_000);
    expect(addWeighed(emptyBasket, mince, 100, 100)).toMatchObject({ ok: false, code: "weight.tare_invalid" });
  });

  it("changes and removes lines, and zero removes", () => {
    let b = must(addItem(emptyBasket, bread, 3));
    b = must(setQuantity(b, "p-bread", 5));
    expect(b.entries[0]!.quantity).toBe(5);
    expect(must(setQuantity(b, "p-bread", 0)).entries).toHaveLength(0);
    expect(removeItem(b, "p-bread").entries).toHaveLength(0);
    expect(setQuantity(b, "nope", 1)).toMatchObject({ ok: false, code: "basket.line_not_found" });
    expect(setQuantity(b, "p-bread", -1)).toMatchObject({ ok: false, code: "basket.quantity_invalid" });
  });
});

describe("scanning", () => {
  it("adds an ordinary barcode, asks for the scale for a weighed item and says so for a barcode it does not know", () => {
    const first = must(scan(emptyBasket, " 6001000000011 ", catalogue, null));
    expect(first.kind === "item" && first.basket.entries[0]!.quantity).toBe(1);

    expect(must(scan(emptyBasket, "123456", catalogue, null))).toMatchObject({ kind: "weigh" });
    expect(scan(emptyBasket, "9999999999999", catalogue, null)).toMatchObject({ ok: false, code: "basket.unknown_item" });
  });

  it("reads a scale label for the item and its weight", () => {
    const label = buildEmbeddedBarcode(rules, "123456", 1_250);

    const scanned = must(scan(emptyBasket, label, catalogue, rules));

    expect(scanned.kind === "item" && scanned.basket.entries[0]).toMatchObject({ productId: "p-mince", quantity: 1_250, unitDivisor: 1000 });
    expect(scan(emptyBasket, buildEmbeddedBarcode(rules, "999999", 500), catalogue, rules)).toMatchObject({ ok: false, code: "basket.unknown_item" });
  });

  it("takes a price label as exactly that price, merges a second label and will not mix it with an ordinary add", () => {
    const priceRules: EmbeddedBarcodeRules = { prefix: "20", itemDigits: 5, valueDigits: 5, valueType: "price", decimals: 2 };
    const catalogue2 = [{ ...bread, barcode: "00042" }];
    const label = buildEmbeddedBarcode(priceRules, "00042", 4_599);

    const once = must(scan(emptyBasket, label, catalogue2, priceRules));
    const twice = must(scan(once.kind === "item" ? once.basket : emptyBasket, label, catalogue2, priceRules));

    expect(twice.kind === "item" && twice.basket.entries).toHaveLength(1);
    expect(twice.kind === "item" && summariseBasket(twice.basket, [], now).totalCents).toBe(9_198);
    expect(twice.kind === "item" && addItem(twice.basket, catalogue2[0]!)).toMatchObject({ ok: false, code: "basket.label_mixed" });
    expect(twice.kind === "item" && setQuantity(twice.basket, "p-bread", 3)).toMatchObject({ ok: false, code: "basket.label_line" });
    const plain = must(addItem(emptyBasket, catalogue2[0]!));
    expect(scan(plain, label, catalogue2, priceRules)).toMatchObject({ ok: false, code: "basket.label_mixed" });
  });
});

describe("the sale as the customer sees it", () => {
  const tenOff: PromotionDef = {
    id: "00000000-0000-0000-0000-000000000001", name: "10% off bread", type: "percent_off", productIds: ["p-bread"], percent: 10, amountCents: 0, items: [],
    startsAt: new Date("2026-10-01T00:00:00Z"), endsAt: new Date("2026-12-01T00:00:00Z"), days: [0, 1, 2, 3, 4, 5, 6],
  };

  it("adds up the lines and takes the VAT out of the total", () => {
    const b = must(addItem(must(addItem(emptyBasket, bread, 2)), milk));

    const s = summariseBasket(b, [], now);

    expect(s).toMatchObject({ subtotalCents: 5_799, discountCents: 0, totalCents: 5_799, dueCents: 5_799, vatCents: 756 });
  });

  it("applies running promotions to ordinary lines only and names the promotion on the line", () => {
    let b = must(addItem(emptyBasket, bread, 2));
    b = must(addWeighed(b, mince, 1_000));

    const s = summariseBasket(b, [{ ...tenOff, productIds: [] }, tenOff], now);

    const line = s.lines.find((l) => l.productId === "p-bread")!;
    expect(line).toMatchObject({ discountCents: 360, totalCents: 3_240, promotionId: tenOff.id });
    expect(s.lines.find((l) => l.productId === "p-mince")).toMatchObject({ discountCents: 0, promotionId: null });   // never discounted by a per-unit promotion
    expect(s.totalCents).toBe(3_240 + 12_000);
  });

  it("adds a tip on top of the total, which the VAT does not cover", () => {
    const b = must(setTip(must(addItem(emptyBasket, bread)), 200));

    expect(summariseBasket(b, [], now)).toMatchObject({ totalCents: 1_800, tipCents: 200, dueCents: 2_000, vatCents: 235 });
    expect(setTip(b, -1)).toMatchObject({ ok: false, code: "basket.tip_invalid" });
  });
});

describe("making the sale record", () => {
  const basket = must(addItem(must(addItem(emptyBasket, bread, 2)), milk));
  const summary = summariseBasket(basket, [], now);
  const pay = (p: Payment[], tip = 0) => toSaleUpload({ ...basket, tipCents: tip }, summariseBasket({ ...basket, tipCents: tip }, [], now), p, ids, now);

  it("makes a plain cash sale with the tendered cash and the change worked out", () => {
    const sale = must(pay([{ method: "cash", amountCents: 5_799, tenderedCents: 10_000 }]));

    expect(sale).toMatchObject({ id: "sale-1", paymentMethodCode: "cash", totalCents: 5_799, vatCents: 756, tenderedCents: 10_000, changeCents: 4_201, payments: null, tipCents: 0 });
    expect(sale.lines).toHaveLength(2);
    expect(sale.soldAt).toBe("2026-10-26T08:00:00.000Z");
  });

  it("includes the tip in what must be paid and takes the change from the whole amount", () => {
    const sale = must(pay([{ method: "cash", amountCents: 5_999, tenderedCents: 6_000 }], 200));

    expect(sale).toMatchObject({ totalCents: 5_799, tipCents: 200, tenderedCents: 6_000, changeCents: 1 });
  });

  it("makes a split sale from several payments and the cash part carries the change", () => {
    const sale = must(pay([{ method: "card", amountCents: 3_000 }, { method: "cash", amountCents: 2_799, tenderedCents: 3_000 }]));

    expect(sale.paymentMethodCode).toBe("split");
    expect(sale.payments).toEqual([
      { methodCode: "card", amountCents: 3_000, reference: null, tenderedCents: null },
      { methodCode: "cash", amountCents: 2_799, reference: null, tenderedCents: 3_000 },
    ]);
    expect(sale.tenderedCents).toBeNull();
  });

  it("makes a sale paid by wallet or voucher a split sale of one part and asks for its reference", () => {
    expect(must(pay([{ method: "wallet", amountCents: 5_799, reference: "w-1" }])).paymentMethodCode).toBe("split");
    expect(pay([{ method: "voucher", amountCents: 5_799 }])).toMatchObject({ ok: false, code: "payment.reference_required" });
  });

  it("refuses payments that do not add up or break the rules the server enforces", () => {
    expect(pay([{ method: "card", amountCents: 5_000 }])).toMatchObject({ ok: false, code: "sale.payment_mismatch" });
    expect(pay([{ method: "card", amountCents: 6_000 }])).toMatchObject({ ok: false, code: "sale.payment_mismatch" });
    expect(pay([{ method: "cash", amountCents: 5_799, tenderedCents: 5_000 }])).toMatchObject({ ok: false, code: "sale.tender_invalid" });
    expect(pay([{ method: "card", amountCents: 5_799, tenderedCents: 6_000 }])).toMatchObject({ ok: false, code: "sale.tender_invalid" });
    expect(pay([{ method: "card", amountCents: 3_000 }, { method: "card", amountCents: 2_799 }])).toMatchObject({ ok: false, code: "payment.method_repeated" });
    expect(pay([{ method: "credit", amountCents: 5_799 }])).toMatchObject({ ok: false, code: "sale.account_required" });
    expect(pay([{ method: "credit", amountCents: 3_000, accountId: "a" }, { method: "cash", amountCents: 2_799 }])).toMatchObject({ ok: false, code: "sale.split_credit" });
    expect(pay([{ method: "cash", amountCents: 0 }])).toMatchObject({ ok: false, code: "payment.amount_invalid" });
    expect(pay([])).toMatchObject({ ok: false, code: "sale.payment_missing" });
    expect(toSaleUpload(emptyBasket, summariseBasket(emptyBasket, [], now), [{ method: "cash", amountCents: 1 }], ids, now)).toMatchObject({ ok: false, code: "sale.no_lines" });
  });

  it("makes a credit sale for one account and a tip never more than the bill", () => {
    const sale = must(pay([{ method: "credit", amountCents: 5_799, accountId: "acc-1" }]));
    expect(sale).toMatchObject({ paymentMethodCode: "credit", accountId: "acc-1", tenderedCents: null, changeCents: null });
    expect(pay([{ method: "cash", amountCents: 5_799 + 6_000 }], 6_000)).toMatchObject({ ok: false, code: "sale.tip_invalid" });
    expect(summary.dueCents).toBe(5_799);
  });

  it("sends the promotion with a discounted line, as the server insists", () => {
    const promo: PromotionDef = { id: "00000000-0000-0000-0000-000000000001", name: "10%", type: "percent_off", productIds: [], percent: 10, amountCents: 0, items: [], startsAt: new Date("2026-10-01T00:00:00Z"), endsAt: new Date("2026-12-01T00:00:00Z"), days: [0, 1, 2, 3, 4, 5, 6] };
    const s = summariseBasket(basket, [promo], now);

    const sale = must(toSaleUpload(basket, s, [{ method: "card", amountCents: s.dueCents }], ids, now));

    expect(sale.lines.every((l) => l.discountCents > 0 && l.promotionId === promo.id)).toBe(true);
    expect(sale.totalCents).toBe(sale.lines.reduce((sum, l) => sum + l.priceCents * l.quantity - l.discountCents, 0));   // the server checks exactly this
  });

  it("sends a weighed line as grams at a price per kilogram", () => {
    const b = must(addWeighed(emptyBasket, mince, 1_250));
    const s = summariseBasket(b, [], now);

    const sale = must(toSaleUpload(b, s, [{ method: "card", amountCents: s.dueCents }], ids, now));

    expect(sale.lines[0]).toMatchObject({ quantity: 1_250, priceCents: 12_000, unitDivisor: 1000, discountCents: 0 });
    expect(sale.totalCents).toBe(15_000);
  });
});

describe("a manager's price override", () => {
  const promo: PromotionDef = { id: "00000000-0000-0000-0000-000000000001", name: "10%", type: "percent_off", productIds: [], percent: 10, amountCents: 0, items: [], startsAt: new Date("2026-10-01T00:00:00Z"), endsAt: new Date("2026-12-01T00:00:00Z"), days: [0, 1, 2, 3, 4, 5, 6] };

  it("turns a lower price into a discount on the line with the approver as the reason", () => {
    const b = must(setPriceOverride(must(addItem(emptyBasket, bread, 2)), "p-bread", 1_500, "mgr-1"));
    const s = summariseBasket(b, [], now);

    expect(s.lines[0]).toMatchObject({ discountCents: 600, promotionId: null, priceOverrideBy: "mgr-1", totalCents: 3_000 });
    expect(s.totalCents).toBe(3_000);
    const sale = must(toSaleUpload(b, s, [{ method: "card", amountCents: 3_000 }], ids, now));
    expect(sale.lines[0]).toMatchObject({ priceCents: 1_800, discountCents: 600, promotionId: null, priceOverrideBy: "mgr-1" });
    expect(sale.totalCents).toBe(sale.lines.reduce((sum, l) => sum + l.priceCents * l.quantity - l.discountCents, 0));
  });

  it("follows the quantity and a weighed line, and leaves other lines without an approver", () => {
    let b = must(addItem(must(addItem(emptyBasket, milk)), bread));
    b = must(setPriceOverride(b, "p-bread", 1_000, "mgr-1"));
    b = must(setQuantity(b, "p-bread", 3));
    const s = summariseBasket(b, [], now);
    expect(s.lines.find((l) => l.productId === "p-bread")).toMatchObject({ discountCents: 2_400 });
    const sale = must(toSaleUpload(b, s, [{ method: "card", amountCents: s.dueCents }], ids, now));
    expect("priceOverrideBy" in sale.lines.find((l) => l.productId === "p-milk")!).toBe(false);

    const w = must(setPriceOverride(must(addWeighed(emptyBasket, mince, 1_000)), "p-mince", 10_000, "mgr-1"));
    expect(summariseBasket(w, [], now).lines[0]).toMatchObject({ discountCents: 2_000, totalCents: 10_000 });
  });

  it("is the line's discount instead of a promotion, not on top of it", () => {
    const b = must(setPriceOverride(must(addItem(emptyBasket, bread)), "p-bread", 1_000, "mgr-1"));

    expect(summariseBasket(b, [promo], now).lines[0]).toMatchObject({ discountCents: 800, promotionId: null, priceOverrideBy: "mgr-1" });
  });

  it("only ever lowers the price, never below zero, and not on a labelled line", () => {
    const b = must(addItem(emptyBasket, bread));

    expect(setPriceOverride(b, "p-bread", 1_800, "m")).toMatchObject({ ok: false, code: "basket.override_not_lower" });
    expect(setPriceOverride(b, "p-bread", 2_500, "m")).toMatchObject({ ok: false, code: "basket.override_not_lower" });
    expect(setPriceOverride(b, "p-bread", -1, "m")).toMatchObject({ ok: false, code: "basket.override_invalid" });
    expect(setPriceOverride(b, "p-bread", 10.5, "m")).toMatchObject({ ok: false, code: "basket.override_invalid" });
    expect(setPriceOverride(b, "p-bread", 100, " ")).toMatchObject({ ok: false, code: "basket.override_approver" });
    expect(setPriceOverride(b, "nope", 100, "m")).toMatchObject({ ok: false, code: "basket.line_not_found" });
    const priceRules: EmbeddedBarcodeRules = { prefix: "20", itemDigits: 5, valueDigits: 5, valueType: "price", decimals: 2 };
    const labelled = must(scan(emptyBasket, buildEmbeddedBarcode(priceRules, "00042", 4_599), [{ ...bread, barcode: "00042" }], priceRules));
    expect(labelled.kind === "item" && setPriceOverride(labelled.basket, "p-bread", 100, "m")).toMatchObject({ ok: false, code: "basket.label_line" });
    expect(must(setPriceOverride(b, "p-bread", 0, "m")).entries[0]!.overrideUnitPriceCents).toBe(0);
  });

  it("can be taken off again", () => {
    const b = must(setPriceOverride(must(addItem(emptyBasket, bread)), "p-bread", 1_000, "mgr-1"));
    const cleared = clearPriceOverride(b, "p-bread");

    expect(summariseBasket(cleared, [], now).lines[0]).toMatchObject({ discountCents: 0, priceOverrideBy: null, totalCents: 1_800 });
    expect("overrideApprovedBy" in cleared.entries[0]!).toBe(false);
  });
});
