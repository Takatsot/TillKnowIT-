import { describe, expect, it } from "vitest";
import { formatRand, parseRandToCents, profitCents, suggestedPrice, vatIncluded } from "./money";
import { cancellationMovements, checkMovement, needsCount, onHand } from "./stock";

describe("money", () => {
  it("parses rand text to whole cents", () => {
    expect(parseRandToCents("18")).toBe(1800);
    expect(parseRandToCents("18.5")).toBe(1850);
    expect(parseRandToCents("R 18,50")).toBe(1850);
    expect(parseRandToCents("18.555")).toBeNull();
    expect(parseRandToCents("abc")).toBeNull();
    expect(parseRandToCents("-5")).toBeNull();
  });

  it("formats cents as rand", () => {
    expect(formatRand(1850).replace(/\s/g, " ")).toMatch(/R\s?18[.,]50/);
  });

  it("finds the VAT inside a total and suggests tidy prices", () => {
    expect(vatIncluded(11500, 15)).toBe(1500);
    expect(vatIncluded(1800, 0)).toBe(0);
    expect(suggestedPrice(1400, 25)).toBe(1750);
    expect(suggestedPrice(1401, 25)).toBe(1760);
    expect(profitCents(1800, 1400, 3)).toBe(1200);
  });
});

describe("stock", () => {
  it("signs follow the movement type, like the server", () => {
    const ok = (type: Parameters<typeof checkMovement>[0]["type"], quantity: number, extra = {}) =>
      checkMovement({ productId: "p", type, quantity, reasonCode: "counted", saleId: "s", ...extra }).ok;
    expect(ok("receive", 5)).toBe(true);
    expect(ok("receive", -5)).toBe(false);
    expect(ok("write_off", -2)).toBe(true);
    expect(ok("count", -3)).toBe(true);
    expect(ok("cancel", -2)).toBe(false);
    expect(ok("receive", 0)).toBe(false);
  });

  it("counts and write-offs need a reason; sale and cancel need their sale", () => {
    const code = (m: Parameters<typeof checkMovement>[0]) => {
      const r = checkMovement(m);
      return r.ok ? "ok" : r.code;
    };
    expect(code({ productId: "p", type: "count", quantity: 1 })).toBe("stock.reason_required");
    expect(code({ productId: "p", type: "sale", quantity: -1 })).toBe("stock.sale_required");
  });

  it("stock on hand is the sum and may go negative", () => {
    const stock = onHand([
      { productId: "bread", quantity: 10 }, { productId: "bread", quantity: -4 }, { productId: "milk", quantity: -2 },
    ]);
    expect(stock.get("bread")).toBe(6);
    expect(needsCount(stock.get("milk")!)).toBe(true);
    expect(needsCount(stock.get("bread")!)).toBe(false);
  });

  it("cancelling a sale puts every line back", () => {
    const moves = cancellationMovements("sale-1", [{ productId: "bread", quantity: 2 }, { productId: "milk", quantity: 1 }]);
    expect(moves.map((m) => [m.type, m.quantity, m.saleId])).toEqual([["cancel", 2, "sale-1"], ["cancel", 1, "sale-1"]]);
    expect(moves.every((m) => checkMovement(m).ok)).toBe(true);
  });
});
