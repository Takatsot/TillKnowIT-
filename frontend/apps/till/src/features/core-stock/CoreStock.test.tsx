// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ok, fail, type RecordOutboxEntry } from "@tillknowit/domain";
import { afterEach, describe, expect, it, vi } from "vitest";
import { makeCoreCtx, SHOP } from "../../coreTesting";
import { CoreStock } from "./CoreStock";
import { feature } from "./index";
import type { StockRow } from "./stockData";

afterEach(cleanup);

const row = (productId: string, name: string, onHand: number, flags: string[] = []): StockRow => ({ productId, name, barcode: null, onHand, lowStockLevel: 0, expiryDate: null, daysLeft: null, suggestedOrderQuantity: 0, flags });
const rows = [row("p-bread", "Bread", 10, ["low"]), row("p-milk", "Milk 1L", 4)];
const stockApi = () => ({ get: vi.fn(async (path: string) => (path === `/shops/${SHOP}/stock` ? ok(rows) : fail("x", "unexpected"))) }) as never;

const movements = async (outbox: { all: () => Promise<unknown[]> }) => ((await outbox.all()) as RecordOutboxEntry[]).filter((e) => e.kind === "stock_movement").map((e) => e.record as Record<string, unknown>);

describe("Stock tab", () => {
  it("is for the owner and the manager, and needs no connection", () => {
    expect(feature.roles).toEqual(["owner", "manager"]);
    expect(feature.requiresOnline).toBeFalsy();
  });

  it("shows the stock list from the server with its notes and keeps a copy", async () => {
    const { ctx, kv } = makeCoreCtx({ api: stockApi() });
    render(<CoreStock ctx={ctx} />);

    expect(await screen.findByText("Bread")).toBeTruthy();
    expect(screen.getByText("Low")).toBeTruthy();
    expect(await kv.get("stock.rows")).toMatchObject({ rows: [{ productId: "p-bread" }, { productId: "p-milk" }] });
  });

  it("shows the kept figures, and says so, when the till is offline", async () => {
    const { ctx, kv } = makeCoreCtx({ online: false });
    await kv.set("stock.rows", { rows, asOf: "2026-10-26T08:00:00.000Z" });
    render(<CoreStock ctx={ctx} />);

    expect(await screen.findByText("Milk 1L")).toBeTruthy();
    expect(screen.getByText(/offline, so these are the figures/)).toBeTruthy();
  });

  it("receives a delivery as one movement per product with the supplier and invoice", async () => {
    const { ctx, outbox, nudgeSync } = makeCoreCtx({ api: stockApi() });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await screen.findByText("Bread");

    await user.click(screen.getByRole("button", { name: "Receive stock" }));
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");
    const quantity = screen.getByRole("textbox", { name: "Quantity of Bread" });
    await user.clear(quantity);
    await user.type(quantity, "12");
    await user.type(screen.getByRole("textbox", { name: "Supplier (optional)" }), "Acme");
    await user.type(screen.getByRole("textbox", { name: "Invoice number (optional)" }), "INV-7");
    await user.click(screen.getByRole("button", { name: "Save delivery" }));

    await waitFor(() => expect(screen.getByText("Delivery saved: 1 products.")).toBeTruthy());
    expect(await movements(outbox)).toEqual([expect.objectContaining({ productId: "p-bread", type: "receive", quantity: 12, unitCostCents: 1200, supplierName: "Acme", invoiceRef: "INV-7", staffId: ctx.staff.id })]);
    expect(nudgeSync).toHaveBeenCalled();
  });

  it("will not save a delivery with a bad quantity", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: stockApi() });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await user.click(screen.getByRole("button", { name: "Receive stock" }));
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");
    const quantity = screen.getByRole("textbox", { name: "Quantity of Bread" });
    await user.clear(quantity);
    await user.type(quantity, "0");

    await user.click(screen.getByRole("button", { name: "Save delivery" }));

    expect(screen.getByRole("alert").textContent).toBe("Every quantity must be a whole number above zero.");
    expect(await movements(outbox)).toEqual([]);
  });

  it("turns a count that is short into a write-off with its reason, and one that is over into a count", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: stockApi() });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await screen.findByText("Bread");
    await user.click(screen.getByRole("button", { name: "Count" }));

    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");
    expect(screen.getByText("The till has 10 of Bread.")).toBeTruthy();
    await user.type(screen.getByRole("textbox", { name: "Counted" }), "7");
    await user.selectOptions(screen.getByRole("combobox", { name: "Reason" }), "damaged");
    await user.click(screen.getByRole("button", { name: "Save count" }));
    await waitFor(() => expect(screen.getByText("Saved. Bread: -3 (now 7).")).toBeTruthy());

    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");
    expect(screen.getByText("The till has 7 of Bread.")).toBeTruthy();   // the figure moved with the movement
    await user.type(screen.getByRole("textbox", { name: "Counted" }), "9");
    await user.click(screen.getByRole("button", { name: "Save count" }));
    await waitFor(() => expect(screen.getByText("Saved. Bread: +2 (now 9).")).toBeTruthy());

    expect(await movements(outbox)).toEqual([
      expect.objectContaining({ type: "write_off", quantity: -3, reasonCode: "damaged" }),
      expect.objectContaining({ type: "count", quantity: 2, reasonCode: "damaged" }),
    ]);
  });

  it("says nothing changes when the count matches", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: stockApi() });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await screen.findByText("Bread");
    await user.click(screen.getByRole("button", { name: "Count" }));
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000028{Enter}");

    await user.type(screen.getByRole("textbox", { name: "Counted" }), "4");
    await user.click(screen.getByRole("button", { name: "Save count" }));

    expect(screen.getByText("The count matches. Nothing to change.")).toBeTruthy();
    expect(await movements(outbox)).toEqual([]);
  });

  it("cannot count a product whose figure the till has never had", async () => {
    const { ctx } = makeCoreCtx({ online: false });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await user.click(screen.getByRole("button", { name: "Count" }));

    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");

    expect(screen.getByText(/does not know how many Bread/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Save count" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("writes off stock with a reason", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: stockApi() });
    const user = userEvent.setup();
    render(<CoreStock ctx={ctx} />);
    await user.click(screen.getByRole("button", { name: "Write off" }));
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "milk");
    await user.click(screen.getByRole("button", { name: "Milk 1L" }));

    await user.type(screen.getByRole("textbox", { name: "How many to write off" }), "2");
    await user.selectOptions(screen.getByRole("combobox", { name: "Reason" }), "expired");
    await user.click(screen.getByRole("button", { name: "Confirm write-off" }));

    await waitFor(() => expect(screen.getByText("Written off: 2 of Milk 1L.")).toBeTruthy());
    expect(await movements(outbox)).toEqual([expect.objectContaining({ productId: "p-milk", type: "write_off", quantity: -2, reasonCode: "expired" })]);
  });

  it("says when the server cannot be reached and still shows what it has", async () => {
    const api = { get: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) } as never;
    const { ctx, kv } = makeCoreCtx({ api });
    await kv.set("stock.rows", { rows, asOf: "2026-10-26T08:00:00.000Z" });
    render(<CoreStock ctx={ctx} />);

    expect(await screen.findByText("We cannot reach the server.")).toBeTruthy();
    expect(within(screen.getByRole("table")).getByText("Bread")).toBeTruthy();
  });
});
