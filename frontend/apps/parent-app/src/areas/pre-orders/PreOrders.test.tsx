// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, thandi } from "../../shared/testing";
import { PreOrders } from "./PreOrders";

afterEach(cleanup);

const menu = {
  breaks: [{ code: "first", name: "First break", cutOff: "08:30" }],
  items: [{ id: "m1", name: "Cheese sandwich", priceCents: 1500, productId: null, categoryCode: "lunch", isAvailable: true }],
};
const order = {
  id: "o1", holderId: "h1", breakCode: "first", collectOn: "2026-10-30", status: "Placed", totalCents: 3000, cutOffAt: "2026-10-30T06:30:00Z",
  lines: [{ menuItemId: "m1", name: "Cheese sandwich", quantity: 2, priceCents: 1500 }],
};

function api(orders: unknown = ok([order]), extra: Record<string, unknown> = {}) {
  const get = vi.fn(async (path: string) => (path === "/me/children" ? ok([thandi]) : path.endsWith("/menu") ? ok(menu) : orders));
  return { get: get as never, ...extra };
}

describe("PreOrders", () => {
  it("shows the menu and my orders with their status", async () => {
    mount(<PreOrders />, api());

    expect(await screen.findByText("First break on 2026-10-30")).toBeTruthy();
    expect(screen.getByText("Placed")).toBeTruthy();
    expect(screen.getByText("2 x Cheese sandwich")).toBeTruthy();
    expect(screen.getByLabelText("Cheese sandwich: how many?")).toBeTruthy();
  });

  it("places an order for a break with the chosen quantities", async () => {
    const post = vi.fn(async () => ok(order));
    mount(<PreOrders />, api(ok([]), { post }));
    const user = userEvent.setup({ delay: null });
    const qty = await screen.findByLabelText("Cheese sandwich: how many?");
    expect((screen.getByRole("button", { name: "Place order" }) as HTMLButtonElement).disabled).toBe(true);

    await user.clear(qty);
    await user.type(qty, "2");
    expect(screen.getByText(rand("Total: R30,00"), { exact: false })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Place order" }));

    await waitFor(() => expect(screen.getByText("Order placed. Total", { exact: false })).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/pre-orders", expect.objectContaining({ callerPersonId: "p1", holderId: "h1", breakCode: "first", lines: [{ menuItemId: "m1", quantity: 2 }] }));
  });

  it("shows the server's refusal when the cut-off has passed", async () => {
    const post = vi.fn(async () => fail("preorder.cut_off_passed", "Orders for First break closed at 08:30."));
    mount(<PreOrders />, api(ok([]), { post }));
    const user = userEvent.setup({ delay: null });
    const qty = await screen.findByLabelText("Cheese sandwich: how many?");

    await user.clear(qty);
    await user.type(qty, "1");
    await user.click(screen.getByRole("button", { name: "Place order" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Orders for First break closed at 08:30.");
  });

  it("says so when there are no orders", async () => {
    mount(<PreOrders />, api(ok([])));

    expect(await screen.findByText("No orders in the last week or coming up.")).toBeTruthy();
  });

  it("cancels an order and says the money is back", async () => {
    const post = vi.fn(async () => ok(undefined));
    mount(<PreOrders />, api(ok([order]), { post }));
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Cancel the First break order for 2026-10-30" }));

    await waitFor(() => expect(screen.getByText("The order was cancelled and the money is back in the wallet.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/pre-orders/o1/cancel", undefined, { callerPersonId: "p1" });
  });

  it("shows the server's refusal when the order can no longer be cancelled", async () => {
    const post = vi.fn(async () => fail("preorder.cut_off_passed", "The cut-off has passed, so this order can no longer be cancelled."));
    mount(<PreOrders />, api(ok([order]), { post }));
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Cancel the First break order for 2026-10-30" }));

    expect((await screen.findByRole("alert")).textContent).toBe("The cut-off has passed, so this order can no longer be cancelled.");
  });
});
