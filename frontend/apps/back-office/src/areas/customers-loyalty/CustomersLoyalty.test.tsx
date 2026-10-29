// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { withCaller } from "../selling-common/testing";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CustomersLoyalty } from "./CustomersLoyalty";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const thandi = { id: "c1", name: "Thandi", phone: "0821111111", email: null, messageConsent: false, consentAt: null, points: 120 };
const dup = { id: "c2", name: "Thandi M", phone: "0822222222", email: null, messageConsent: true, consentAt: null, points: 5 };
const points = { customerId: "c1", points: 120, valueCents: 1200, minRedeemPoints: 50, recent: [{ type: "earn", points: 120, note: "Sale", at: "2026-11-01T10:00:00Z", saleId: null }] };
const quote = { id: "q1", number: "Q-1", customerName: "Sipho", customerPhone: null, lines: [], totalCents: 50000, expiresOn: "2026-12-01", status: "open", ticketId: null };
const layby = {
  id: "l1", reference: "LB-1", customerName: "Nomsa", customerPhone: null, totalCents: 100000, paidCents: 20000, balanceCents: 80000, dueOn: "2026-12-31",
  status: "active", cancellationFeeCents: 0, refundDueCents: 0, payments: [],
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={withCaller(api)} initial={session}><CustomersLoyalty /></SessionProvider>);
}

const get = (extra: Record<string, unknown> = {}) => vi.fn(async (path: string, _q?: unknown) => {
  for (const [suffix, value] of Object.entries(extra)) if (path.endsWith(suffix)) return value;
  if (path === "/shops/s1/products") return ok([{ id: "tv", name: "Television", barcode: null, categoryCode: "electronics", costCents: 100000, priceCents: 150000, isActive: true }]);
  if (path.endsWith("/customers")) return ok([thandi, dup]);
  if (path.endsWith("/points")) return ok(points);
  if (path.endsWith("/quotes")) return ok([quote]);
  if (path.endsWith("/laybys")) return ok([layby]);
  if (path.endsWith("/reservations")) return ok({ tv: 1 });
  return fail("x", `unexpected ${path}`);
}) as never;

async function findThandi(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Name or phone number"), "Thandi");
  await user.click(screen.getByRole("button", { name: "Find" }));
  await screen.findByText("0821111111");
}

describe("Customers and loyalty", () => {
  it("asks for a search first, then lists matches", async () => {
    const g = get();
    mount({ get: g });
    const user = userEvent.setup();
    expect(screen.getByText(/Search to see customers/)).toBeTruthy();
    await findThandi(user);
    expect(g).toHaveBeenCalledWith("/shops/s1/customers", { search: "Thandi", deviceId: "d1", staffId: "p1" });
  });

  it("says when nothing matches", async () => {
    mount({ get: get({ "/customers": ok([]) }) });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Name or phone number"), "Zed");
    await user.click(screen.getByRole("button", { name: "Find" }));
    expect(await screen.findByText("No customer matches that.")).toBeTruthy();
  });

  it("adds a customer with consent recorded", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok(thandi));
    mount({ get: get(), post: post as never });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Name"), "Thandi");
    await user.type(screen.getByLabelText("Phone number"), "0821111111");
    await user.click(screen.getByLabelText("The customer agreed to be sent messages"));
    await user.click(screen.getByRole("button", { name: "Add customer" }));
    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: "d1", staffId: "p1", name: "Thandi", phone: "0821111111", email: null, messageConsent: true, consentSource: "back_office" });
    expect(await screen.findByText("Thandi was added.")).toBeTruthy();
  });

  it("shows points and history, redeems, adjusts with a manager, and merges", async () => {
    const post = vi.fn(async (path: string, ..._rest: unknown[]) => (path.endsWith("/redeem") ? ok({ redemptionReference: "R1", points: 60, valueCents: 600, balanceAfter: 60 }) : ok(thandi)));
    mount({ get: get(), post: post as never });
    const user = userEvent.setup();
    await findThandi(user);
    await user.click(screen.getByRole("button", { name: "Open Thandi" }));
    expect(await screen.findByText(/120 points, worth/)).toBeTruthy();
    expect(screen.getByText("Sale")).toBeTruthy();

    await user.type(screen.getByLabelText("Points to redeem"), "60");
    await user.click(screen.getByRole("button", { name: "Redeem points" }));
    await waitFor(() => expect(post.mock.calls.some((c) => c[0] === "/shops/s1/customers/c1/points/redeem" && (c[1] as { points: number }).points === 60)).toBe(true));
    expect(await screen.findByText(/Redeemed 60 points/)).toBeTruthy();

    await user.type(screen.getByLabelText("Points to add (or take away with a minus)"), "-10");
    await user.type(screen.getByLabelText("Reason"), "Wrong sale");
    await user.type(screen.getByLabelText("Manager's staff id (approves the change)"), "m9");
    await user.click(screen.getByRole("button", { name: "Adjust points" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/customers/c1/points/adjust", { deviceId: "d1", approvedBy: "m9", staffId: "p1", points: -10, reason: "Wrong sale" }));

    await user.selectOptions(screen.getByLabelText("Duplicate to merge in"), "c2");
    await user.click(screen.getByRole("button", { name: "Merge into Thandi" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/customers/c1/merge", { callerPersonId: "p1", sourceCustomerId: "c2" }));
    expect(await screen.findByText("Thandi M was merged into Thandi.")).toBeTruthy();
  });

  it("shows the server's message when a points change is refused", async () => {
    mount({ get: get(), post: vi.fn(async () => fail("permission.denied", "That staff member cannot approve changes.")) as never });
    const user = userEvent.setup();
    await findThandi(user);
    await user.click(screen.getByRole("button", { name: "Open Thandi" }));
    await screen.findByText(/120 points, worth/);
    await user.type(screen.getByLabelText("Points to add (or take away with a minus)"), "5");
    await user.type(screen.getByLabelText("Reason"), "Gift");
    await user.type(screen.getByLabelText("Manager's staff id (approves the change)"), "m9");
    await user.click(screen.getByRole("button", { name: "Adjust points" }));
    expect((await screen.findByRole("alert")).textContent).toBe("That staff member cannot approve changes.");
  });

  it("creates a voucher and shows the code once, then checks and uses it", async () => {
    const post = vi.fn(async (path: string, ..._rest: unknown[]) => {
      if (path.endsWith("/vouchers")) return ok({ id: "v1", code: "ABCD-1234", balanceCents: 10000, expiresOn: null });
      return ok({ id: "v1", codeLast4: "1234", balanceCents: path.endsWith("/redeem") ? 6000 : 10000, expiresOn: null, isVoid: false, isExpired: false });
    });
    mount({ get: get(), post: post as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Gift vouchers" }));

    await user.type(screen.getByLabelText("Amount in rand"), "100");
    await user.click(screen.getByRole("button", { name: "Create voucher" }));
    expect(await screen.findByText("Voucher code: ABCD-1234")).toBeTruthy();
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: "d1", staffId: "p1", amountCents: 10000, paidWith: "cash", saleId: null });

    await user.type(screen.getByLabelText("Voucher code"), "ABCD-1234");
    await user.click(screen.getByRole("button", { name: "Check balance" }));
    expect(await screen.findByText(/Voucher ending 1234/)).toBeTruthy();

    await user.type(screen.getByLabelText("Amount to take off the voucher, in rand"), "40");
    await user.click(screen.getByRole("button", { name: "Use voucher" }));
    await waitFor(() => expect(post.mock.calls.some((c) => c[0] === "/shops/s1/vouchers/redeem" && (c[1] as { amountCents: number }).amountCents === 4000)).toBe(true));
    expect(await screen.findByText(/Taken off/)).toBeTruthy();
  });

  it("lists quotes, converts one and opens its pdf", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok({}));
    const download = vi.fn(async (..._a: unknown[]) => ok({ blob: new Blob(["pdf"]), fileName: "Q-1.pdf" }));
    URL.createObjectURL = vi.fn(() => "blob:x");
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    mount({ get: get(), post: post as never, download: download as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Quotes" }));
    expect(await screen.findByText("Sipho")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Open PDF of Q-1" }));
    await waitFor(() => expect(open).toHaveBeenCalled());
    expect(download).toHaveBeenCalledWith("/shops/s1/quotes/q1/pdf", { deviceId: "d1", staffId: "p1" });

    await user.click(screen.getByRole("button", { name: "Make Q-1 a sale" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/quotes/q1/convert", { deviceId: "d1", staffId: "p1" }));
    expect(await screen.findByText(/Q-1 became a ticket/)).toBeTruthy();
  });

  it("saves a new quote with prices in cents", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok(quote));
    mount({ get: get({ "/quotes": ok([]) }), post: post as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Quotes" }));
    expect(await screen.findByText("No quotes yet.")).toBeTruthy();
    await user.type(screen.getByLabelText("Quote number"), "Q-2");
    fireEvent.change(screen.getByLabelText("Valid until"), { target: { value: "2026-12-01" } });
    await user.selectOptions(await screen.findByLabelText("Product 1"), "tv");
    await user.type(screen.getByLabelText("Unit price in rand 1"), "4999.99");
    await user.click(screen.getByRole("button", { name: "Save quote" }));
    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ number: "Q-2", expiresOn: "2026-12-01", lines: [{ productId: "tv", name: "Television", quantity: 1, priceCents: 499999 }] });
  });

  it("takes a lay-by payment, shows held stock and shows a server refusal on cancel", async () => {
    const post = vi.fn(async (path: string, ..._rest: unknown[]) => (path.endsWith("/cancel") ? fail("layby.not_active", "This lay-by is already finished.") : ok({ ...layby, balanceCents: 60000 })));
    mount({ get: get(), post: post as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Lay-by" }));
    expect(await screen.findByText("Nomsa")).toBeTruthy();
    expect(await screen.findByText("Television")).toBeTruthy();

    await user.type(screen.getByLabelText("Payment for LB-1 in rand"), "200");
    await user.click(screen.getByRole("button", { name: "Take payment for LB-1" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/laybys/l1/payments", { deviceId: "d1", staffId: "p1", amountCents: 20000, methodCode: "cash" }));
    expect(await screen.findByText(/Payment taken\. LB-1 has/)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Cancel LB-1" }));
    expect((await screen.findByRole("alert")).textContent).toBe("This lay-by is already finished.");
  });

  it("shows the empty lay-by state", async () => {
    mount({ get: get({ "/laybys": ok([]), "/reservations": ok({}) }) });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Lay-by" }));
    expect(await screen.findByText("No lay-bys yet.")).toBeTruthy();
    expect(await screen.findByText("Nothing is held.")).toBeTruthy();
  });
});
