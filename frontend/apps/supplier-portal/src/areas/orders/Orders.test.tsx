// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, SUPPLIER } from "../../shared/testing";
import { OrdersArea } from "./OrdersArea";

afterEach(cleanup);

const line = { id: "l1", name: "Maize meal", unitsPerCase: 2, orderedCases: 5, confirmedCases: null, deliveredCases: null, effectiveCases: 5, casePriceCents: 18000, status: "pending" };
const order = {
  id: "o1", reference: "PO-1001", status: "sent", deliveryDate: null, paymentMethod: "on_delivery", paymentStatus: "owing", lines: [line],
  subtotalCents: 90000, vatCents: 13500, deliveryFeeCents: 2500, totalCents: 106000, belowMinimum: false, minOrderCents: 50000,
};

const getOrders = (orders: unknown[]) => vi.fn(async () => ok(orders));

describe("Orders", () => {
  it("lists incoming orders, filtered by status, and says so when there are none", async () => {
    const get = getOrders([order]);
    mount(<OrdersArea />, { get: get as never });
    expect(await screen.findByText("PO-1001")).toBeTruthy();
    expect(get).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/orders`, { callerPersonId: "p1", status: "" });
    cleanup();
    mount(<OrdersArea />, { get: getOrders([]) as never });
    expect(await screen.findByText("No orders here yet.")).toBeTruthy();
  });

  it("confirms with a changed line and sends the decisions", async () => {
    const patch = vi.fn(async () => ok({ ...order, status: "confirmed" }));
    const get = getOrders([order]);
    mount(<OrdersArea />, { get: get as never, patch: patch as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Open PO-1001" }));

    await user.selectOptions(screen.getByLabelText("Maize meal: what will you do?"), "change");
    const cases = screen.getByLabelText("Maize meal: cases");
    await user.clear(cases);
    await user.type(cases, "3");
    await user.click(screen.getByRole("button", { name: "Send reply" }));

    await waitFor(() => expect(screen.getByText("Reply sent for PO-1001.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/purchase-orders/o1/lines", { callerPersonId: "p1", decisions: [{ lineId: "l1", action: "change", cases: 3, casePriceCents: null }], deliveryDate: null });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("marks a confirmed order delivered in full", async () => {
    const post = vi.fn(async () => ok({ ...order, status: "delivered" }));
    mount(<OrdersArea />, { get: getOrders([{ ...order, status: "confirmed" }]) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Open PO-1001" }));
    await user.click(screen.getByRole("button", { name: "Mark delivered" }));

    await waitFor(() => expect(screen.getByText("PO-1001 was marked delivered.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/purchase-orders/o1/deliver", { callerPersonId: "p1", deliveredCases: null });
  });

  it("marks paid with the caller in the query and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("order.paid_in_app", "This order is paid in the app."));
    mount(<OrdersArea />, { get: getOrders([{ ...order, status: "delivered" }]) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Open PO-1001" }));
    await user.click(screen.getByRole("button", { name: "Mark as paid" }));

    expect((await screen.findByText("This order is paid in the app.")).getAttribute("role")).toBe("alert");
    expect(post).toHaveBeenCalledWith("/purchase-orders/o1/mark-paid", undefined, { callerPersonId: "p1" });
  });

  it("raises a dispute on a delivered order", async () => {
    const post = vi.fn(async () => ok({ id: "d1" }));
    mount(<OrdersArea />, { get: getOrders([{ ...order, status: "delivered", paymentStatus: "paid" }]) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Open PO-1001" }));
    await user.type(screen.getByLabelText("What went wrong?"), "Shop refused the pallet");
    await user.click(screen.getByRole("button", { name: "Raise dispute" }));

    await waitFor(() => expect(screen.getByText(/The dispute was raised/)).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/purchase-orders/o1/disputes", { callerPersonId: "p1", reason: "Shop refused the pallet" });
  });

  it("downloads the statement", async () => {
    const download = vi.fn(async () => ok({ blob: new Blob(["a"]), fileName: "statement.csv" }));
    const save = vi.fn();
    mount(<OrdersArea save={save} />, { get: getOrders([]) as never, download: download as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Statement" }));
    await user.click(screen.getByRole("button", { name: "Download statement" }));

    await waitFor(() => expect(screen.getByText("Statement statement.csv was downloaded.")).toBeTruthy());
    expect(download).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/statement`, { callerPersonId: "p1", from: "", to: "", format: "csv" });
    expect(save).toHaveBeenCalled();
  });

  it("shows ratings, and explains when there are none", async () => {
    const get = vi.fn(async (path: string) => ok(path.endsWith("/ratings") ? { count: 12, averageScore: 4.25, onTimePercent: 90, completePercent: 95, recentComments: ["Fast delivery"] } : []));
    mount(<OrdersArea />, { get: get as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Ratings" }));
    expect(await screen.findByText("Average score: 4.3 out of 5")).toBeTruthy();
    expect(screen.getByText("Fast delivery")).toBeTruthy();
  });

  it("shows demand with the minimum shops explained", async () => {
    const get = vi.fn(async (path: string) => ok(path.endsWith("/demand")
      ? { minShops: 5, areas: [{ areaCode: "JHB", products: [{ supplierProductId: "sp1", name: "Maize meal", shopsLow: 7 }] }] } : []));
    mount(<OrdersArea />, { get: get as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Demand" }));
    expect(await screen.findByText("Maize meal")).toBeTruthy();
    expect(screen.getByText(/fewer than 5 shops is left out/)).toBeTruthy();
  });

  it("shows an error when the orders cannot be loaded", async () => {
    mount(<OrdersArea />, { get: vi.fn(async () => fail("permission.denied", "You cannot see these orders.")) as never });
    expect((await screen.findByText("You cannot see these orders.")).getAttribute("role")).toBe("alert");
  });
});
