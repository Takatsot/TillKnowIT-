// @vitest-environment jsdom
import { fail, isSaleEntry, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { Tables } from "./Tables";

afterEach(cleanup);

const flags = ["tables", "open_tickets"];
const t4 = { id: "t-4", area: "Patio", name: "T4", seats: 4 };
const ticket = (over = {}) => ({
  id: "k1", label: "T4", tableId: "t-4", status: "open", lines: [{ productId: "p-bread", name: "Bread", quantity: 2, priceCents: 1800, unitDivisor: 1 }],
  totalCents: 3600, heldByDeviceId: DEVICE, heldUntil: "2099-01-01T00:00:00Z", lockedForYou: false, ...over,
});

describe("tables feature", () => {
  it("is switched on by the tables flag and needs the server", () => {
    expect(feature.flag).toBe("tables");
    expect(feature.requiresOnline).toBe(true);
    expect(feature.id.startsWith("core-")).toBe(false);
  });

  it("shows the floor plan and says so when there are no tables", async () => {
    const { ctx } = makeCtx({ flags, api: { get: vi.fn(async () => ok([])) as never } });
    render(<Tables ctx={ctx} />);

    expect(await screen.findByText(/No tables on the floor plan yet/)).toBeTruthy();
  });

  it("opens a ticket on a free table with its items, then takes cash and closes it with the sale id", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async (path: string) => (path.endsWith("floor-plan") ? ok([t4]) : ok([])));
    const post = vi.fn(async (path: string, body?: unknown) => {
      if (path.endsWith("/tickets")) return ok(ticket({ lines: (body as { lines: unknown[] }).lines }));
      return ok(ticket({ status: "closed" }));
    });
    const { ctx, outbox, nudgeSync } = makeCtx({ flags, api: { get, post } as never });
    render(<Tables ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /Table T4, Free/ }));
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
    await user.click(screen.getByRole("button", { name: "Add Bread" }));
    await user.click(screen.getByRole("button", { name: "One more Bread" }));
    await user.click(screen.getByRole("button", { name: "Open ticket" }));

    const opened = post.mock.calls.find((c) => String(c[0]).endsWith("/tickets"))!;
    expect(opened[1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF, label: "T4", tableId: "t-4" });
    expect((opened[1] as { lines: Array<{ quantity: number }> }).lines[0]!.quantity).toBe(2);

    await user.click(await screen.findByRole("button", { name: "Take payment" }));
    await user.type(screen.getByRole("textbox", { name: "Cash received" }), "50");
    await user.click(screen.getByRole("button", { name: /Take R\s?36,00 and close the ticket/ }));

    await waitFor(() => expect(screen.getByText(/The ticket is closed/)).toBeTruthy());
    const queued = (await outbox.all()).filter(isSaleEntry);
    expect(queued).toHaveLength(1);
    expect(queued[0]!.sale).toMatchObject({ totalCents: 3600, paymentMethodCode: "cash", tenderedCents: 5000, changeCents: 1400 });
    const closed = post.mock.calls.find((c) => String(c[0]).endsWith("/k1/close"))!;
    expect((closed[1] as { saleId: string }).saleId).toBe(queued[0]!.sale.id);
    expect(nudgeSync).toHaveBeenCalled();
  });

  it("shows a ticket another till is holding as read only and offers to take it when free", async () => {
    const user = userEvent.setup();
    const locked = ticket({ heldByDeviceId: "other", lockedForYou: true });
    const get = vi.fn(async (path: string) => (path.endsWith("floor-plan") ? ok([t4]) : ok([locked])));
    const { ctx } = makeCtx({ flags, api: { get } as never });
    render(<Tables ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /Table T4, Open/ }));

    expect(screen.getByText("Another till is working on this ticket, so it is read only.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Take it to edit" })).toBeNull();
  });

  it("shows the server's refusal when holding fails", async () => {
    const user = userEvent.setup();
    const free = ticket({ heldByDeviceId: null, heldUntil: null });
    const get = vi.fn(async (path: string) => (path.endsWith("floor-plan") ? ok([t4]) : ok([free])));
    const post = vi.fn(async () => fail("ticket.locked", "Another till is working on this ticket."));
    const { ctx } = makeCtx({ flags, api: { get, post } as never });
    render(<Tables ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /Table T4, Open/ }));
    await user.click(screen.getByRole("button", { name: "Take it to edit" }));

    expect((await screen.findByRole("alert")).textContent).toContain("Another till is working on this ticket.");
  });

  it("moves a ticket's items to the sale on the till and voids the ticket", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async (path: string) => (path.endsWith("floor-plan") ? ok([t4]) : ok([ticket()])));
    const post = vi.fn(async () => ok(undefined));
    const addLine = vi.fn(() => ok(undefined));
    const openSell = vi.fn();
    const { ctx } = makeCtx({ flags, api: { get, post } as never, sale: { addLine, addProduct: vi.fn(), setCustomer: vi.fn() }, openSell });
    render(<Tables ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /Table T4, Open/ }));
    await user.click(screen.getByRole("button", { name: "Move to the sale on the till" }));

    expect(addLine).toHaveBeenCalledWith({ productId: "p-bread", name: "Bread", priceCents: 1800, costCents: 1200, quantity: 2 });
    await waitFor(() => expect(post).toHaveBeenCalledWith(expect.stringContaining("/k1/void"), { deviceId: DEVICE, staffId: STAFF }));
    expect(openSell).toHaveBeenCalled();
    expect(await screen.findByText("T4 was moved to the sale on the till.")).toBeTruthy();
  });

  it("voids only after a second confirmation", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async (path: string) => (path.endsWith("floor-plan") ? ok([t4]) : ok([ticket()])));
    const post = vi.fn(async () => ok(undefined));
    const { ctx } = makeCtx({ flags, api: { get, post } as never });
    render(<Tables ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /Table T4, Open/ }));
    await user.click(screen.getByRole("button", { name: "Void ticket" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, void it" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(expect.stringContaining("/k1/void"), { deviceId: DEVICE, staffId: STAFF }));
    expect(await screen.findByText("The ticket was voided.")).toBeTruthy();
  });

  it("explains when the till is not registered", () => {
    const { ctx } = makeCtx({ flags, device: null });
    render(<Tables ctx={ctx} />);

    expect(screen.getByText(/not registered to a shop yet/)).toBeTruthy();
  });
});
