import { describe, expect, it } from "vitest";
import { checkVas, vasCommission, vasUnavailableReason, type VasDraft } from "./vas";

const code = (d: VasDraft) => {
  const r = checkVas(d);
  return r.ok ? "ok" : r.code;
};

// Same cases as VasTests in C#, so the till and the server give the same answers.
describe("airtime, data and electricity checks (same as the server)", () => {
  it("cleans phone and meter numbers", () => {
    const phone = checkVas({ product: "airtime", recipient: "082 123 4567", amountCents: 2000 });
    expect(phone.ok && phone.value.recipient).toBe("+27821234567");
    const meter = checkVas({ product: "electricity", recipient: "0123-4567-890", amountCents: 5000 });
    expect(meter.ok && meter.value.recipient).toBe("01234567890");
  });

  it("refuses wrong numbers", () => {
    expect(code({ product: "airtime", recipient: "12345", amountCents: 2000 })).toBe("vas.recipient_invalid");
    expect(code({ product: "electricity", recipient: "12345", amountCents: 5000 })).toBe("vas.recipient_invalid");
    expect(code({ product: "electricity", recipient: "0821234567", amountCents: 5000 })).toBe("vas.recipient_invalid");
  });

  it("amounts are whole rand inside the product's limits", () => {
    expect(code({ product: "airtime", recipient: "0821234567", amountCents: 500 })).toBe("ok");
    expect(code({ product: "airtime", recipient: "0821234567", amountCents: 499 })).toBe("vas.amount_invalid");
    expect(code({ product: "airtime", recipient: "0821234567", amountCents: 100_100 })).toBe("vas.amount_invalid");
    expect(code({ product: "airtime", recipient: "0821234567", amountCents: 2050 })).toBe("vas.amount_invalid");
    expect(code({ product: "electricity", recipient: "01234567890", amountCents: 1000 })).toBe("vas.amount_invalid");
  });

  it("data needs a bundle", () => {
    expect(code({ product: "data", recipient: "0821234567", amountCents: 10_000 })).toBe("vas.product_required");
    expect(code({ product: "data", recipient: "0821234567", amountCents: 10_000, productCode: "1GB-30D" })).toBe("ok");
  });

  it("commission rounds down to the cent", () => {
    expect(vasCommission(10_000, 300)).toBe(300);
    expect(vasCommission(10_050, 300)).toBe(301);
    expect(vasCommission(500, 0)).toBe(0);
  });

  it("says plainly why the button does not work offline", () => {
    expect(vasUnavailableReason(true)).toBeNull();
    expect(vasUnavailableReason(false)).toContain("network connection");
  });
});
