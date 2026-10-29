// @vitest-environment jsdom
import { fail, isSaleEntry, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { Quotes } from "./Quotes";

afterEach(cleanup);

const line = { productId: "p-bread", name: "Bread", quantity: 2, priceCents: 1800, unitDivisor: 1 };
const quote = (over = {}) => ({ id: "q1", number: "Q-1", customerName: "Anele", customerPhone: "0821234567", lines: [line], totalCents: 3600, expiresOn: "2026-11-10", status: "open", ticketId: null, ...over });
const ticket = { id: "k9", label: "Quote Q-1", tableId: null, status: "open", lines: [line], totalCents: 3600, heldByDeviceId: DEVICE, heldUntil: "2099-01-01T00:00:00Z", lockedForYou: false };

describe("quotes feature", () => {
  it("is switched on by the quotes flag", () => {
    expect(feature.flag).toBe("quotes");
    expect(feature.requiresOnline).toBe(true);
  });

  it("lists quotes with their status and says so when there are none", async () => {
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([quote(), quote({ id: "q2", number: "Q-2", status: "converted" })])) as never } });
    render(<Quotes ctx={ctx} />);

    expect(await screen.findByText("Q-1")).toBeTruthy();
    expect(screen.getByText("Turned into a sale")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Convert Q-2 to a sale" })).toBeNull();
    cleanup();

    const empty = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([])) as never } });
    render(<Quotes ctx={empty.ctx} />);
    expect(await screen.findByText("No quotes yet.")).toBeTruthy();
  });

  it("creates a quote from items", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async () => ok([]));
    const post = vi.fn(async (_path: string, _body?: unknown) => ok(quote()));
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get, post } as never });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "New quote" }));
    await user.type(screen.getByRole("textbox", { name: "Customer name" }), "Anele");
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
    await user.click(screen.getByRole("button", { name: "Add Bread" }));
    await user.click(screen.getByRole("button", { name: "Save quote" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![0]).toContain("/quotes");
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF, customerName: "Anele", lines: [{ productId: "p-bread", quantity: 1 }] });
    expect(await screen.findByText(/saved\./)).toBeTruthy();
  });

  it("asks for items before saving", async () => {
    const user = userEvent.setup();
    const post = vi.fn();
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([])), post } as never });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "New quote" }));
    await user.click(screen.getByRole("button", { name: "Save quote" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Add at least one item.");
    expect(post).not.toHaveBeenCalled();
  });

  it("converts a quote to a ticket and takes payment for it", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async () => ok([quote()]));
    const post = vi.fn(async (path: string) => ok(path.endsWith("/convert") ? ticket : { ...ticket, status: "closed" }));
    const { ctx, outbox } = makeCtx({ flags: ["quotes"], api: { get, post } as never });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Convert Q-1 to a sale" }));
    await user.click(await screen.findByRole("button", { name: "Take payment now" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Paid by" }), "card");
    await user.click(screen.getByRole("button", { name: /Take R\s?36,00 and close the ticket/ }));

    await waitFor(() => expect(screen.getByText(/Quote Q-1 was paid/)).toBeTruthy());
    const sales = (await outbox.all()).filter(isSaleEntry);
    expect(sales[0]!.sale).toMatchObject({ totalCents: 3600, paymentMethodCode: "card" });
    expect(post.mock.calls.some((c) => String(c[0]).endsWith("/k9/close"))).toBe(true);
  });

  it("cancels only after confirming and shows a server refusal", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail("quote.not_open", "This quote is no longer open."));
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([quote()])), post } as never });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Cancel Q-1" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, cancel it" }));

    expect((await screen.findByRole("alert")).textContent).toBe("This quote is no longer open.");
    expect(within(document.body).queryByText("Quote Q-1 was cancelled.")).toBeNull();
  });
});

describe("quote to the sale on the till", () => {
  it("adds an open quote's goods to the sale at the quoted prices and shows the Sell tab", async () => {
    const user = userEvent.setup();
    const addLine = vi.fn(() => ok(undefined));
    const openSell = vi.fn();
    const post = vi.fn(async (_path: string, _body?: unknown) => ok(undefined));
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([quote(), quote({ id: "q2", number: "Q-2", status: "converted" })])), post } as never, sale: { addLine, addProduct: vi.fn(), setCustomer: vi.fn() }, openSell });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Add Q-1 to the sale on the till" }));

    expect(addLine).toHaveBeenCalledWith({ productId: "p-bread", name: "Bread", priceCents: 1800, costCents: 1200, quantity: 2 });
    expect(openSell).toHaveBeenCalled();
    expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/quotes/q1/to-sale`, expect.objectContaining({ deviceId: DEVICE, staffId: STAFF }));
    expect(await screen.findByText(/Q-1 was added to the sale and is now marked/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Add Q-2 to the sale on the till" })).toBeNull();
  });

  it("says which item could not be added and stays on the quotes tab", async () => {
    const user = userEvent.setup();
    const openSell = vi.fn();
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([quote()])) as never }, sale: { addLine: vi.fn(() => fail("sale.screen_missing", "The sell screen is not ready.")), addProduct: vi.fn(), setCustomer: vi.fn() }, openSell });
    render(<Quotes ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Add Q-1 to the sale on the till" }));

    expect(screen.getByText("Bread could not be added: The sell screen is not ready.")).toBeTruthy();
    expect(openSell).not.toHaveBeenCalled();
  });

  it("does not offer it when the till has no sale screen to add to", async () => {
    const { ctx } = makeCtx({ flags: ["quotes"], api: { get: vi.fn(async () => ok([quote()])) as never } });
    render(<Quotes ctx={ctx} />);

    await screen.findByText("Q-1");
    expect(screen.queryByRole("button", { name: /to the sale on the till/ })).toBeNull();
  });
});
