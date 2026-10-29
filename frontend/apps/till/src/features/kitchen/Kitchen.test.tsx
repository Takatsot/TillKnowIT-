// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { FakeKitchenDisplay, FakePrinter } from "@tillknowit/hardware";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { Kitchen } from "./Kitchen";

afterEach(cleanup);

const order = (over = {}) => ({
  id: "o1", orderNo: "A12", tableLabel: "T4", status: "new", items: [{ name: "Burger", quantity: 2, note: "no onion" }], createdAt: "2026-10-26T10:00:00Z", readyAt: null, ...over,
});

describe("kitchen feature", () => {
  it("is switched on by the kitchen flag", () => {
    expect(feature.flag).toBe("kitchen");
    expect(feature.requiresOnline).toBe(true);
  });

  it("shows orders in their columns and advances one", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async () => ok([order(), order({ id: "o2", orderNo: "A13", status: "ready" })]));
    const post = vi.fn(async () => ok(order({ status: "preparing" })));
    const { ctx } = makeCtx({ flags: ["kitchen"], api: { get, post } as never });
    render(<Kitchen ctx={ctx} />);

    const fresh = within(await screen.findByRole("region", { name: "New orders" }));
    expect(fresh.getByText("2 x Burger (no onion)")).toBeTruthy();
    expect(within(screen.getByRole("region", { name: "Ready to collect" })).getByText("Order A13")).toBeTruthy();

    await user.click(fresh.getByRole("button", { name: "Start A12" }));

    expect(post).toHaveBeenCalledWith(expect.stringContaining("/kitchen-orders/o1/advance"), { deviceId: DEVICE, staffId: STAFF, action: "start" });
  });

  it("says so when the kitchen is empty", async () => {
    const { ctx } = makeCtx({ flags: ["kitchen"], api: { get: vi.fn(async () => ok([])) as never } });
    render(<Kitchen ctx={ctx} />);

    expect(await screen.findByText("Nothing is waiting in the kitchen.")).toBeTruthy();
  });

  it("sends an order and prints the kitchen ticket", async () => {
    const user = userEvent.setup();
    const printer = new FakePrinter();
    const get = vi.fn(async () => ok([]));
    const post = vi.fn(async (_p: string, body?: unknown) => ok(order({ orderNo: (body as { orderNo: string }).orderNo })));
    const { ctx } = makeCtx({ flags: ["kitchen"], hardware: { printer }, api: { get, post } as never });
    render(<Kitchen ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Send to the kitchen" }));
    await user.clear(screen.getByRole("textbox", { name: "Order number" }));
    await user.type(screen.getByRole("textbox", { name: "Order number" }), "A99");
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
    await user.click(screen.getByRole("button", { name: "Add Bread" }));
    await user.type(screen.getByRole("textbox", { name: "Note for Bread" }), "toasted");
    await user.click(screen.getAllByRole("button", { name: "Send to the kitchen" })[1]!);

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ orderNo: "A99", items: [{ name: "Bread", quantity: 1, note: "toasted" }] });
    await waitFor(() => expect(printer.jobs).toHaveLength(1));
    expect(await screen.findByText("Order A99 sent to the kitchen and printed.")).toBeTruthy();
  });

  it("sends to the kitchen screen when there is one", async () => {
    const user = userEvent.setup();
    const kitchen = new FakeKitchenDisplay();
    const post = vi.fn(async () => ok(order()));
    const { ctx } = makeCtx({ flags: ["kitchen"], hardware: { kitchen }, api: { get: vi.fn(async () => ok([])), post } as never });
    render(<Kitchen ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Send to the kitchen" }));
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "milk");
    await user.click(screen.getByRole("button", { name: "Add Milk 1L" }));
    await user.click(screen.getAllByRole("button", { name: "Send to the kitchen" })[1]!);

    await waitFor(() => expect(kitchen.orders).toHaveLength(1));
  });

  it("asks for another number when the order number is taken", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail("kitchen.order_no_taken", "That order number is already used."));
    const { ctx } = makeCtx({ flags: ["kitchen"], api: { get: vi.fn(async () => ok([])), post } as never });
    render(<Kitchen ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Send to the kitchen" }));
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
    await user.click(screen.getByRole("button", { name: "Add Bread" }));
    await user.click(screen.getAllByRole("button", { name: "Send to the kitchen" })[1]!);

    expect((await screen.findByRole("alert")).textContent).toContain("already used");
  });
});
