// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Reordering } from "./Reordering";

afterEach(cleanup);
vi.setConfig({ testTimeout: 30000 });

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };

const offer = {
  position: 1, supplierId: "sup1", supplierName: "Cash and Carry", supplierProductId: "sp1", cases: 2, unitsReceived: 24, totalCostCents: 24000,
  trueCostPerUnitCents: 1000, marginPercent: 30, isCheapest: true, isUsual: false, lastPriceCents: null, meetsMinimum: true, deliveryDaysAway: 2,
  rating: 4.5, dealTitle: null, label: null,
};
const item = { productId: "pr1", productName: "Maize meal 5kg", onHand: 2, daysLeft: 3, quantityUnits: 24, flags: ["low"], offerCount: 1, best: offer, cheapest: offer };
const order = {
  id: "o1", supplierName: "Cash and Carry", reference: "PO-001", status: "draft", channel: "app", paymentMethod: "on_delivery", paymentStatus: "owing",
  lines: [{ id: "l1", name: "Maize meal 5kg", orderedCases: 2, confirmedCases: null, deliveredCases: null, effectiveCases: 2, casePriceCents: 12000, status: "ordered" }],
  totalCents: 24000, hasChanges: false, receivedAt: null,
};
const deal = { id: "g1", supplierName: "Mill Co", productName: "Flour 10kg", unitsPerCase: 4, casePriceCents: 9000, minTotalCases: 50, totalCases: 20, progressPercent: 40, closesAt: "2026-11-30T10:00:00Z", status: "open", yourCases: null };

function routes(overrides: Record<string, unknown> = {}) {
  return vi.fn(async (path: string) => {
    if (path in overrides) return overrides[path];
    if (path === "/shops/s1/order-list") return ok([item]);
    if (path === "/shops/s1/offers") return ok({ productId: "pr1", productName: "Maize meal 5kg", quantityUnits: 24, sellingPriceCents: 1500, offers: [offer] });
    if (path === "/shops/s1/purchase-orders") return ok([order]);
    if (path === "/shops/s1/group-deals") return ok([deal]);
    if (path === "/shops/s1/sharing") return ok({ mode: "usual" });
    if (path === "/shops/s1/usual-suppliers") return ok([{ supplierId: "sup1", name: "Cash and Carry", lastOrderAt: "2026-10-20T08:00:00Z" }, { supplierId: "sup2", name: "Mill Co", lastOrderAt: null }]);
    return fail("test.unexpected", path);
  });
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Reordering /></SessionProvider>);
}

describe("Reordering", () => {
  it("shows the order list and builds orders from the chosen products", async () => {
    const post = vi.fn(async () => ok([{ order, warning: null }]));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Maize meal 5kg")).toBeTruthy();
    expect(screen.getByText(/Cash and Carry: 2 cases, R/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Build orders" }) as HTMLButtonElement).disabled).toBe(true);

    await user.click(screen.getByLabelText("Add Maize meal 5kg to the order"));
    await user.click(screen.getByRole("button", { name: "Build orders" }));

    await waitFor(() => expect(screen.getByText(/1 draft orders were built/)).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/purchase-orders/build", {
      callerPersonId: "p1", paymentMethod: "on_delivery", selections: [{ supplierId: "sup1", supplierProductId: "sp1", cases: 2 }],
    });
  });

  it("shows offers for a product and makes a supplier usual", async () => {
    const get = routes();
    const put = vi.fn(async () => ok(undefined));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "See offers for Maize meal 5kg" }));
    expect(await screen.findByText("Offers for Maize meal 5kg")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/shops/s1/offers", { product: "pr1", callerPersonId: "p1" });

    await user.click(screen.getByRole("button", { name: "Make Cash and Carry the usual supplier" }));
    await waitFor(() => expect(screen.getByText("Cash and Carry is now a usual supplier.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/usual-suppliers", { callerPersonId: "p1", supplierId: "sup1" });
  });

  it("shows the empty state of the order list", async () => {
    mount({ get: routes({ "/shops/s1/order-list": ok([]) }) as never });
    expect(await screen.findByText("Nothing needs reordering right now.")).toBeTruthy();
  });

  it("sends a draft order and shows a server refusal when cancelling fails", async () => {
    const post = vi.fn(async (path: string) => (path.endsWith("/cancel") ? fail("order.cannot_cancel", "This order can no longer be cancelled.") : ok({ ...order, status: "sent" })));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Your orders" }));
    await user.click(await screen.findByRole("button", { name: "Send order PO-001" }));
    await user.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(screen.getByText("Order PO-001 was sent.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/purchase-orders/o1/send", { callerPersonId: "p1", channel: "app" });

    await user.click(screen.getByRole("button", { name: "Cancel order PO-001" }));
    expect((await screen.findByRole("alert")).textContent).toBe("This order can no longer be cancelled.");
  });

  it("confirms what arrived with counted cases", async () => {
    const delivered = { ...order, status: "delivered" };
    const post = vi.fn(async () => ok(delivered));
    mount({ get: routes({ "/shops/s1/purchase-orders": ok([delivered]) }) as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Your orders" }));
    await user.click(await screen.findByRole("button", { name: "Confirm delivery of order PO-001" }));
    const input = screen.getByLabelText("Maize meal 5kg: cases received (ordered 2)");
    await user.clear(input);
    await user.type(input, "1");
    await user.click(screen.getByRole("button", { name: "Save what arrived" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/purchase-orders/o1/receive", { callerPersonId: "p1", receivedCases: { l1: 1 } }));
  });

  it("joins a group deal with a quantity and checks the quantity first", async () => {
    const post = vi.fn(async () => ok({ ...deal, yourCases: 3 }));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Group deals" }));
    expect(await screen.findByText("20 of 50 cases (40%)")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Join Flour 10kg" }));
    expect(screen.getByText("Enter a whole number of cases, 1 or more.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Cases for Flour 10kg"), "3");
    await user.click(screen.getByRole("button", { name: "Join Flour 10kg" }));
    await waitFor(() => expect(screen.getByText("You joined with 3 cases of Flour 10kg.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/group-deals/g1/commitments", { callerPersonId: "p1", cases: 3 });
  });

  it("loads and saves the sharing mode", async () => {
    const put = vi.fn(async () => ok({ mode: "all" }));
    mount({ get: routes() as never, put: put as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Suppliers and sharing" }));
    const select = (await screen.findByLabelText("Share with")) as HTMLSelectElement;
    await waitFor(() => expect(select.value).toBe("usual"));
    await user.selectOptions(select, "all");
    await user.click(screen.getByRole("button", { name: "Save sharing" }));

    await waitFor(() => expect(screen.getByText("Sharing was saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/sharing", { callerPersonId: "p1", mode: "all" });
  });

  it("lists the usual suppliers with their last order, from the server", async () => {
    const get = routes();
    mount({ get: get as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Suppliers and sharing" }));

    const list = await screen.findByRole("list", { name: "Usual suppliers" });
    expect(list.textContent).toContain("Cash and Carry: last order");
    expect(list.textContent).toContain("Mill Co: no order yet");
    expect(get).toHaveBeenCalledWith("/shops/s1/usual-suppliers", { callerPersonId: "p1" });
  });

  it("removes a usual supplier and reloads the list", async () => {
    const del = vi.fn(async () => ok({}));
    mount({ get: routes() as never, delete: del as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Suppliers and sharing" }));
    await user.click(await screen.findByRole("button", { name: "Remove Mill Co from usual suppliers" }));

    await waitFor(() => expect(screen.getByText("Mill Co is no longer a usual supplier.")).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/shops/s1/usual-suppliers/sup2", { callerPersonId: "p1" });
  });

  it("says so when there are no usual suppliers, and shows the server's refusal", async () => {
    mount({ get: routes({ "/shops/s1/usual-suppliers": ok([]) }) as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Suppliers and sharing" }));
    expect(await screen.findByText("You have no usual suppliers yet.")).toBeTruthy();
    cleanup();

    mount({ get: routes({ "/shops/s1/usual-suppliers": fail("permission.denied", "You do not have access to this shop.") }) as never });
    await userEvent.setup().click(await screen.findByRole("button", { name: "Suppliers and sharing" }));
    expect(await screen.findByText("You do not have access to this shop.")).toBeTruthy();
  });
});
