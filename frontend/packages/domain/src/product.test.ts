import { describe, expect, it } from "vitest";
import { barcodeTaken, checkProduct, expiredItemsBlockingSale, incomingChangeWins, isBarcode, isExpired, type ProductDraft } from "./product";

const NOW = new Date("2026-10-27T10:00:00Z");
const categories = ["groceries", "drinks"];

const draft = (over: Partial<ProductDraft> = {}): ProductDraft => ({
  name: "Bread", barcode: "6001234567890", categoryCode: "groceries", costCents: 1400, priceCents: 1800,
  lowStockLevel: 5, expiryDate: null, changedAt: NOW, ...over,
});

const codeOf = (d: ProductDraft) => {
  const r = checkProduct(d, NOW, categories);
  return r.ok ? "ok" : r.code;
};

describe("checkProduct", () => {
  it("accepts a good product and tidies the name and barcode", () => {
    const r = checkProduct(draft({ name: "  Bread ", barcode: " 6001234567890 " }), NOW, categories);
    expect(r.ok && r.value.name).toBe("Bread");
    expect(r.ok && r.value.barcode).toBe("6001234567890");
  });

  it("rejects what the server rejects, with the same codes", () => {
    expect(codeOf(draft({ name: "  " }))).toBe("product.name_invalid");
    expect(codeOf(draft({ name: "x".repeat(201) }))).toBe("product.name_invalid");
    expect(codeOf(draft({ barcode: "12ab" }))).toBe("product.barcode_invalid");
    expect(codeOf(draft({ priceCents: -1 }))).toBe("product.price_invalid");
    expect(codeOf(draft({ lowStockLevel: -1 }))).toBe("product.low_stock_invalid");
    expect(codeOf(draft({ categoryCode: "weapons" }))).toBe("product.category_invalid");
    expect(codeOf(draft({ changedAt: new Date("2026-12-01T00:00:00Z") }))).toBe("product.date_invalid");
  });

  it("a missing barcode is fine", () => {
    expect(codeOf(draft({ barcode: null }))).toBe("ok");
    expect(codeOf(draft({ barcode: "" }))).toBe("ok");
  });
});

describe("barcodes and clashes", () => {
  it("recognises 6 to 14 digits", () => {
    expect(isBarcode("6001234567890")).toBe(true);
    expect(isBarcode("12345")).toBe(false);
    expect(isBarcode("123456789012345")).toBe(false);
  });

  it("blocks a barcode used by another product but not by the product itself", () => {
    const products = [{ id: "a", barcode: "6001234567890" }, { id: "b", barcode: null }];
    expect(barcodeTaken("6001234567890", "c", products)).toBe(true);
    expect(barcodeTaken("6001234567890", "a", products)).toBe(false);
    expect(barcodeTaken(null, "c", products)).toBe(false);
  });

  it("last change wins, and a tie keeps what is already there", () => {
    const t = new Date("2026-10-27T09:00:00Z");
    expect(incomingChangeWins(new Date("2026-10-27T09:00:01Z"), t)).toBe(true);
    expect(incomingChangeWins(t, t)).toBe(false);
    expect(incomingChangeWins(new Date("2026-10-27T08:59:59Z"), t)).toBe(false);
  });

  it("knows what is expired", () => {
    expect(isExpired("2026-10-26", "2026-10-27")).toBe(true);
    expect(isExpired("2026-10-27", "2026-10-27")).toBe(false);
    expect(isExpired(null, "2026-10-27")).toBe(false);
  });
});

describe("selling expired items (E3-5)", () => {
  const today = "2026-10-27";
  it("blocks expired items with no override and names them", () => {
    const blocked = expiredItemsBlockingSale([
      { name: "Milk", expiryDate: "2026-10-26" },
      { name: "Bread", expiryDate: "2026-10-27" },
      { name: "Tins", expiryDate: null },
      { name: "Yoghurt", expiryDate: "2026-10-01", expiryOverrideBy: "manager-1" },
    ], today);
    expect(blocked).toEqual(["Milk"]);
  });
});
