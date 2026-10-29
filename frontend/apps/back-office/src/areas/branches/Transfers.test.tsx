// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { withCaller } from "../selling-common/testing";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Branches } from "./Branches";
import { Transfers } from "./Transfers";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const branches = [
  { shopId: "s1", name: "Head shop", isHead: true, status: "verified", municipalityCode: null },
  { shopId: "s2", name: "Soweto branch", isHead: false, status: "verified", municipalityCode: null },
];
const incoming = {
  id: "t1", fromShopId: "s2", toShopId: "s1", status: "Sent", sentAt: "2026-10-26T08:00:00Z", closedAt: null, note: null,
  lines: [{ productId: "pr1", name: "Maize meal 10kg", quantity: 10, receivedQuantity: null, shortfall: 0 }],
};

function fakeGet(transfers: unknown[]) {
  return vi.fn(async (path: string) => {
    if (path.endsWith("/branches")) return ok(branches);
    if (path === "/shops/s1/products") return ok([{ id: "pr1", name: "Maize meal 10kg", barcode: null, categoryCode: "grocery", costCents: 5000, priceCents: 7000, isActive: true }]);
    return ok(transfers);
  });
}

function mount(ui: React.ReactElement, api: Partial<Api>) {
  render(<SessionProvider api={withCaller(api)} initial={session}>{ui}</SessionProvider>);
}

describe("Transfers", () => {
  it("lists transfers with the in-transit filter sent to the server", async () => {
    const get = fakeGet([incoming]);
    mount(<Transfers />, { get: get as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Maize meal 10kg: Sent 10")).toBeTruthy();
    await user.selectOptions(screen.getByLabelText("Show"), "Sent");

    await waitFor(() => expect(get).toHaveBeenCalledWith("/shops/s1/stock-transfers", { deviceId: "d1", staffId: "p1", status: "Sent" }));
  });

  it("receives with a shortfall and posts the quantities that arrived", async () => {
    const post = vi.fn(async () => ok({ ...incoming, status: "Received" }));
    mount(<Transfers />, { get: fakeGet([incoming]) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Maize meal 10kg: Sent 10");

    await user.click(screen.getByRole("button", { name: "Receive" }));
    const input = screen.getByLabelText("Arrived: Maize meal 10kg");
    await user.clear(input);
    await user.type(input, "8");
    expect(screen.getByText("Short by 2")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Confirm received" }));

    await waitFor(() => expect(screen.getByText("The transfer was received.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/stock-transfers/t1/receive", { deviceId: "d1", staffId: "p1", lines: [{ productId: "pr1", quantity: 8 }] });
  });

  it("does not allow more than was sent", async () => {
    mount(<Transfers />, { get: fakeGet([incoming]) as never });
    const user = userEvent.setup();
    await screen.findByText("Maize meal 10kg: Sent 10");
    await user.click(screen.getByRole("button", { name: "Receive" }));
    const input = screen.getByLabelText("Arrived: Maize meal 10kg");
    await user.clear(input);
    await user.type(input, "11");

    expect(screen.getByText("Enter a whole number from 0 to 10.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Confirm received" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("shows the shortfall the server recorded on a received transfer", async () => {
    const done = { ...incoming, status: "Received", lines: [{ ...incoming.lines[0], receivedQuantity: 7, shortfall: 3 }] };
    mount(<Transfers />, { get: fakeGet([done]) as never });

    expect(await screen.findByText("Maize meal 10kg: 7 / 10 (3 short)")).toBeTruthy();
  });

  it("sends a transfer and shows the server's refusal when it fails", async () => {
    const outgoing = { ...incoming, id: "t2", fromShopId: "s1", toShopId: "s2" };
    const post = vi.fn()
      .mockResolvedValueOnce(fail("transfer.stock_short", "There is not enough stock."))
      .mockResolvedValueOnce(ok(outgoing));
    mount(<Transfers />, { get: fakeGet([]) as never, post: post as never });
    const user = userEvent.setup();
    expect(await screen.findByText("No transfers to show.")).toBeTruthy();
    await screen.findByRole("option", { name: "Soweto branch" });
    await screen.findByRole("option", { name: "Maize meal 10kg" });

    await user.selectOptions(screen.getByLabelText("Send to"), "s2");
    await user.selectOptions(screen.getByLabelText("Product"), "pr1");
    await user.type(screen.getByLabelText("Quantity"), "5");
    await user.click(screen.getByRole("button", { name: "Add item" }));
    await user.click(screen.getByRole("button", { name: "Send transfer" }));
    expect((await screen.findByRole("alert")).textContent).toBe("There is not enough stock.");

    await user.click(screen.getByRole("button", { name: "Send transfer" }));
    await waitFor(() => expect(screen.getByText("The transfer was sent.")).toBeTruthy());
    const [path, body] = post.mock.calls[1] as [string, Record<string, unknown>];
    expect(path).toBe("/shops/s1/stock-transfers");
    expect(body).toMatchObject({ deviceId: "d1", staffId: "p1", toShopId: "s2", lines: [{ productId: "pr1", quantity: 5, toProductId: null }], note: null });
    expect(typeof body.id).toBe("string");
  });

  it("cancels a transfer this shop sent", async () => {
    const outgoing = { ...incoming, id: "t2", fromShopId: "s1", toShopId: "s2" };
    const post = vi.fn(async () => ok({ ...outgoing, status: "Cancelled" }));
    mount(<Transfers />, { get: fakeGet([outgoing]) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Maize meal 10kg: Sent 10");

    await user.click(screen.getByRole("button", { name: "Cancel transfer" }));

    await waitFor(() => expect(screen.getByText("The transfer was cancelled.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/stock-transfers/t2/cancel", { deviceId: "d1", staffId: "p1" });
  });
});

describe("Manager access to all branches", () => {
  it("grants access and says so, and explains the plan when refused", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(fail("plan.feature_not_included", "No."))
      .mockResolvedValueOnce(ok(undefined));
    mount(<Branches />, { get: vi.fn(async () => ok(branches)) as never, post: post as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Manager access" }));

    await user.type(screen.getByLabelText("Person id of the manager"), "p9");
    await user.click(screen.getByRole("button", { name: "Give access" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Managing many branches is part of the Pro plan.");

    await user.click(screen.getByRole("button", { name: "Give access" }));
    await waitFor(() => expect(screen.getByText("That person can now manage all branches.")).toBeTruthy());
    expect(post).toHaveBeenLastCalledWith("/organisations/s1/all-branches-access", { callerPersonId: "p1", personId: "p9" });
  });
});
