import { describe, expect, it } from "vitest";
import { businessDayOf, businessDayStart, cashDifference, cashTaken, expectedCash } from "./cashup";
import {
  balanceOf, checkCreditEntry, creditCancelEntry, creditSaleEntry, overLimit, wouldExceedLimit, type CreditEntryDraft,
} from "./credit";

const code = (e: CreditEntryDraft) => {
  const r = checkCreditEntry(e);
  return r.ok ? "ok" : r.code;
};

describe("credit entries", () => {
  it("sales are positive; payments and cancellations are negative", () => {
    expect(code({ type: "sale", amountCents: 3600, saleId: "s1" })).toBe("ok");
    expect(code({ type: "sale", amountCents: -3600, saleId: "s1" })).toBe("credit.amount_sign");
    expect(code({ type: "payment", amountCents: -1000, paymentMethod: "cash" })).toBe("ok");
    expect(code({ type: "payment", amountCents: 1000, paymentMethod: "cash" })).toBe("credit.amount_sign");
    expect(code({ type: "cancel", amountCents: -3600, saleId: "s1" })).toBe("ok");
    expect(code({ type: "sale", amountCents: 0, saleId: "s1" })).toBe("credit.amount_zero");
  });

  it("payments have a method and no sale; sales and cancels need their sale", () => {
    expect(code({ type: "payment", amountCents: -100, paymentMethod: "credit" })).toBe("credit.payment_method");
    expect(code({ type: "payment", amountCents: -100 })).toBe("credit.payment_method");
    expect(code({ type: "payment", amountCents: -100, paymentMethod: "cash", saleId: "s1" })).toBe("credit.sale_unexpected");
    expect(code({ type: "sale", amountCents: 100 })).toBe("credit.sale_required");
  });

  it("builds the charge and the refund for a credit sale, which cancel each other out", () => {
    const charge = creditSaleEntry("s1", 3600);
    const refund = creditCancelEntry("s1", 3600);
    expect(code(charge)).toBe("ok");
    expect(code(refund)).toBe("ok");
    expect(balanceOf([charge.amountCents, refund.amountCents])).toBe(0);
  });

  it("balance is the sum, and the limit only warns", () => {
    expect(balanceOf([3600, -1000])).toBe(2600);
    expect(overLimit(60000, 50000)).toBe(true);
    expect(overLimit(50000, 50000)).toBe(false);
    expect(overLimit(999999, null)).toBe(false);
    expect(wouldExceedLimit(48000, 3600, 50000)).toBe(true);
    expect(wouldExceedLimit(40000, 3600, 50000)).toBe(false);
  });
});

describe("business day (fixed UTC+2)", () => {
  it("rolls over at 22:00 UTC", () => {
    expect(businessDayOf(new Date("2026-10-26T21:59:00Z"))).toBe("2026-10-26");
    expect(businessDayOf(new Date("2026-10-26T22:00:00Z"))).toBe("2026-10-27");
    expect(businessDayStart("2026-10-26").toISOString()).toBe("2026-10-25T22:00:00.000Z");
  });
});

describe("cash-up", () => {
  const day = "2026-10-26";
  const at = new Date("2026-10-26T16:00:00Z");
  const sale = (id: string, totalCents: number, soldAt: string) => ({ id, totalCents, soldAt: new Date(soldAt) });

  it("expected is float + cash sales + cash paid on credit, leaving out cancelled sales", () => {
    const taken = cashTaken({
      day, cashedUpAt: at,
      sales: [sale("a", 3600, "2026-10-26T08:00:00Z"), sale("b", 2500, "2026-10-26T09:00:00Z"), sale("c", 9999, "2026-10-26T10:00:00Z")],
      cancelledSaleIds: new Set(["c"]),
      creditCashPayments: [{ amountCents: -1000, createdAt: new Date("2026-10-26T11:00:00Z") }],
    });

    expect(taken).toEqual({ cashSalesCents: 6100, creditCashCents: 1000 });
    expect(expectedCash(5000, taken.cashSalesCents, taken.creditCashCents)).toBe(12100);
    expect(cashDifference(12000, 12100)).toBe(-100);
  });

  it("only counts the business day up to the moment of the cash-up", () => {
    const taken = cashTaken({
      day, cashedUpAt: at,
      sales: [
        sale("yesterday", 1000, "2026-10-25T21:59:00Z"),
        sale("today", 2000, "2026-10-25T22:00:00Z"),
        sale("after", 4000, "2026-10-26T16:00:01Z"),
      ],
      cancelledSaleIds: new Set(),
      creditCashPayments: [],
    });
    expect(taken.cashSalesCents).toBe(2000);
  });
});
