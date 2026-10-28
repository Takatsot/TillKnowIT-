// @vitest-environment jsdom
import { buildEmbeddedBarcode, formatRand, MemoryOutbox, type CatalogueItem, type EmbeddedBarcodeRules, type PromotionDef } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SellScreen } from "./SellScreen";
import type { StaffRecord } from "./staffStore";

afterEach(cleanup);

const staff: StaffRecord = { id: "0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b", name: "Thandi", role: "cashier", pinHash: "x" };
const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
const milk: CatalogueItem = { id: "p-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false };
const mince: CatalogueItem = { id: "p-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true };
const catalogue = [bread, milk, mince];
const rules: EmbeddedBarcodeRules = { prefix: "2", itemDigits: 6, valueDigits: 5, valueType: "weight", decimals: 3 };
const now = new Date("2026-10-26T08:00:00Z");

function setup(over: { promotions?: PromotionDef[]; barcodeRules?: EmbeddedBarcodeRules | null } = {}) {
  const outbox = new MemoryOutbox();
  const onSaleQueued = vi.fn();
  const user = userEvent.setup();
  render(<SellScreen staff={staff} language="en" catalogue={catalogue} promotions={over.promotions ?? []} barcodeRules={over.barcodeRules ?? null} outbox={outbox} onSaleQueued={onSaleQueued} clock={() => now} />);
  return { outbox, onSaleQueued, user };
}

const entry = () => screen.getByRole("textbox", { name: "Scan or type a barcode or name" });
const scanCode = async (user: ReturnType<typeof userEvent.setup>, code: string) => {
  await user.clear(entry());
  await user.type(entry(), `${code}{Enter}`);
};
/** Elements whose text contains this amount as the till writes money, whatever kind of space the locale puts in it. */
const money = (cents: number) => {
  const wanted = formatRand(cents).replace(/\s/g, " ");
  return screen.getAllByText((_, el) => (el?.children.length === 0 || el?.tagName === "DD" || el?.tagName === "BUTTON") && (el?.textContent ?? "").replace(/\s/g, " ").includes(wanted));
};

describe("SellScreen", () => {
  it("starts empty and will not take payment with nothing in the sale", () => {
    setup();

    expect(screen.getByText("Nothing in this sale yet.")).toBeTruthy();
    expect((screen.getByRole("button", { name: /^Pay/ }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("adds a scanned item once per scan and shows the total with the VAT in it", async () => {
    const { user } = setup();

    await scanCode(user, "6001000000011");
    await scanCode(user, "6001000000011");
    await scanCode(user, "6001000000028");

    expect(screen.getByText("Bread")).toBeTruthy();
    expect(screen.getByText("× 2")).toBeTruthy();
    expect(money(5_799).length).toBeGreaterThan(0);
    expect(screen.getByText("VAT included")).toBeTruthy();
  });

  it("says so when a barcode is not known", async () => {
    const { user } = setup();

    await scanCode(user, "9999999999999");

    expect(screen.getByRole("status").textContent).toBe("We do not have that barcode.");
    expect(screen.getByText("Nothing in this sale yet.")).toBeTruthy();
  });

  it("finds an item by name and adds it from the list", async () => {
    const { user } = setup();

    await user.type(entry(), "mil");
    await user.click(screen.getByRole("button", { name: /Milk 1L/ }));

    expect(screen.getByText("Milk 1L")).toBeTruthy();
  });

  it("changes the quantity with plus and minus and removes a line", async () => {
    const { user } = setup();
    await scanCode(user, "6001000000011");

    await user.click(screen.getByRole("button", { name: "One more: Bread" }));
    await user.click(screen.getByRole("button", { name: "One more: Bread" }));
    expect(screen.getByText("× 3")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "One less: Bread" }));
    expect(screen.getByText("× 2")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Remove: Bread" }));
    expect(screen.getByText("Nothing in this sale yet.")).toBeTruthy();
  });

  it("asks for the weight of an item sold by weight and prices it per kilogram", async () => {
    const { user } = setup();

    await scanCode(user, "123456");
    await user.type(screen.getByRole("textbox", { name: "Weight of Beef mince in grams" }), "1250");
    await user.click(screen.getByRole("button", { name: "Add weight" }));

    expect(screen.getByText("1250 g")).toBeTruthy();
    expect(money(15_000).length).toBeGreaterThan(0);
  });

  it("refuses a weight that is not allowed and stays on the weight step", async () => {
    const { user } = setup();
    await scanCode(user, "123456");

    await user.type(screen.getByRole("textbox", { name: "Weight of Beef mince in grams" }), "99999");
    await user.click(screen.getByRole("button", { name: "Add weight" }));

    expect(screen.getByText("That weight is not allowed.")).toBeTruthy();
  });

  it("reads a scale label with the weight in it", async () => {
    const { user } = setup({ barcodeRules: rules });

    await scanCode(user, buildEmbeddedBarcode(rules, "123456", 500));

    expect(screen.getByText("500 g")).toBeTruthy();
  });

  it("shows a running promotion as a discount", async () => {
    const tenOff: PromotionDef = {
      id: "00000000-0000-0000-0000-000000000001", name: "10% off", type: "percent_off", productIds: [], percent: 10, amountCents: 0, items: [],
      startsAt: new Date("2026-10-01T00:00:00Z"), endsAt: new Date("2026-12-01T00:00:00Z"), days: [0, 1, 2, 3, 4, 5, 6],
    };
    const { user } = setup({ promotions: [tenOff] });

    await scanCode(user, "6001000000011");

    expect(screen.getByText("Discount")).toBeTruthy();
    expect(money(1_620).length).toBeGreaterThan(0);
  });

  it("takes exact cash, queues the sale before saying it is done and clears the basket", async () => {
    const { user, outbox, onSaleQueued } = setup();
    await scanCode(user, "6001000000011");
    await scanCode(user, "6001000000011");

    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Cash" }));
    await user.click(screen.getByRole("button", { name: "Exact" }));

    await waitFor(() => expect(onSaleQueued).toHaveBeenCalled());
    const [queued] = await outbox.all();
    expect(queued).toMatchObject({ state: "pending", sale: { paymentMethodCode: "cash", totalCents: 3_600, tenderedCents: 3_600, changeCents: 0, staffId: staff.id, soldAt: now.toISOString() } });
    expect(queued!.sale.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(screen.getByRole("status").textContent).toContain("Sale saved");
    expect(screen.getByText("Nothing in this sale yet.")).toBeTruthy();
  });

  it("works out the change from the cash received", async () => {
    const { user, outbox } = setup();
    await scanCode(user, "6001000000011");

    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Cash" }));
    await user.type(screen.getByRole("textbox", { name: "Cash received" }), "20");
    await user.click(screen.getByRole("button", { name: /^Change:/ }));

    const [queued] = await outbox.all();
    expect(queued!.sale).toMatchObject({ totalCents: 1_800, tenderedCents: 2_000, changeCents: 200 });
    expect(screen.getByRole("status").textContent).toContain("Change:");
  });

  it("will not accept less cash than the total", async () => {
    const { user, outbox } = setup();
    await scanCode(user, "6001000000011");
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Cash" }));

    await user.type(screen.getByRole("textbox", { name: "Cash received" }), "10");

    expect((screen.getByRole("button", { name: "Pay" }) as HTMLButtonElement).disabled).toBe(true);
    expect(await outbox.all()).toEqual([]);
  });

  it("pays by card in one tap", async () => {
    const { user, outbox } = setup();
    await scanCode(user, "6001000000028");

    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));

    const [queued] = await outbox.all();
    expect(queued!.sale).toMatchObject({ paymentMethodCode: "card", totalCents: 2_199, tenderedCents: null });
  });

  it("goes back from payment to the basket without losing it, and clears the sale on request", async () => {
    const { user } = setup();
    await scanCode(user, "6001000000011");

    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText("Bread")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Clear sale" }));
    expect(screen.getByText("Nothing in this sale yet.")).toBeTruthy();
  });
});
