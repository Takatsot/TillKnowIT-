import { describe, expect, it } from "vitest";
import { checkSale, quickCashOptions, totalOf, type SaleDraft, type SaleLine } from "./sale";

const NOW = new Date("2026-10-26T10:00:00Z");
const bread: SaleLine = { productId: "p-bread", name: "Bread", quantity: 2, priceCents: 1800, costCents: 1400 };
const milk: SaleLine = { productId: "p-milk", name: "Milk 1l", quantity: 1, priceCents: 2500, costCents: 2100 };

const cash = (over: Partial<SaleDraft> = {}): SaleDraft => ({
  paymentMethod: "cash", vatCents: 0, tenderedCents: 10000, accountId: null, soldAt: NOW, lines: [bread, milk], ...over,
});

const codeOf = (draft: SaleDraft) => {
  const r = checkSale(draft, NOW);
  return r.ok ? "ok" : r.code;
};

describe("checkSale", () => {
  it("works out the total and the change itself", () => {
    const r = checkSale(cash(), NOW);
    expect(r.ok && r.value.totalCents).toBe(6100);
    expect(r.ok && r.value.changeCents).toBe(3900);
    expect(totalOf([bread, milk])).toBe(6100);
  });

  it("cash must cover the total", () => {
    expect(codeOf(cash({ tenderedCents: 6000 }))).toBe("sale.tender_invalid");
    expect(codeOf(cash({ tenderedCents: null }))).toBe("sale.tender_invalid");
    expect(codeOf(cash({ tenderedCents: 6100 }))).toBe("ok");
  });

  it("card has no tendered amount and credit needs an account", () => {
    expect(codeOf(cash({ paymentMethod: "card", tenderedCents: null }))).toBe("ok");
    expect(codeOf(cash({ paymentMethod: "card", tenderedCents: 100 }))).toBe("sale.tender_invalid");
    expect(codeOf(cash({ paymentMethod: "credit", tenderedCents: null }))).toBe("sale.account_required");
    expect(codeOf(cash({ paymentMethod: "credit", tenderedCents: null, accountId: "a1" }))).toBe("ok");
    expect(codeOf(cash({ paymentMethod: "card", tenderedCents: null, accountId: "a1" }))).toBe("sale.account_unexpected");
  });

  it("rejects the same things the server rejects, with the same codes", () => {
    expect(codeOf(cash({ lines: [] }))).toBe("sale.no_lines");
    expect(codeOf(cash({ lines: [{ ...bread, quantity: 0 }] }))).toBe("sale.line_quantity");
    expect(codeOf(cash({ lines: [{ ...bread, priceCents: -1 }] }))).toBe("sale.line_price");
    expect(codeOf(cash({ lines: [{ ...bread, name: " " }] }))).toBe("sale.line_name");
    expect(codeOf(cash({ lines: [bread, bread] }))).toBe("sale.duplicate_line");
    expect(codeOf(cash({ paymentMethod: "cheque" }))).toBe("sale.payment_method");
    expect(codeOf(cash({ vatCents: 99999 }))).toBe("sale.vat_invalid");
    expect(codeOf(cash({ soldAt: new Date("2026-10-28T10:00:00Z") }))).toBe("sale.date_invalid");
    expect(codeOf(cash({ soldAt: new Date("2026-10-26T13:00:00Z") }))).toBe("ok");
  });
});

describe("quickCashOptions", () => {
  it("offers the exact amount and the notes that cover it", () => {
    expect(quickCashOptions(6100)).toEqual([6100, 10000, 20000]);
    expect(quickCashOptions(10000)).toEqual([10000, 20000]);
    expect(quickCashOptions(900)).toEqual([900, 1000, 2000, 5000]);
    expect(quickCashOptions(25000)).toEqual([25000, 30000, 40000]);
  });
});
