// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PayLater, type CreditSummary } from "./PayLater";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const empty: CreditSummary = { consents: [], applications: [], draws: [], shared: [] };
const full: CreditSummary = {
  consents: [{ id: "c1", partnerCode: "lendco", months: 6, grantedAt: "2026-10-01T08:00:00Z", withdrawnAt: null, active: true }],
  applications: [{ id: "a1", partnerCode: "lendco", status: "Approved", requestedCents: 500000, approvedCents: 400000, availableCents: 250000, termDays: 30, reason: null, decidedAt: "2026-10-02T08:00:00Z" }],
  draws: [{ id: "d1", orderId: "o1", supplierName: "Metro Wholesale", principalCents: 150000, repayCents: 156000, repaidCents: 0, dueOn: "2026-11-30", status: "Disbursed" }],
  shared: [{ at: "2026-10-01T08:05:00Z", partnerCode: "lendco", months: 6, totalSalesCents: 9876500 }],
};

const orders = [
  { id: "o9", supplierName: "Metro Wholesale", reference: "PO-9", paymentStatus: "owing", totalCents: 120000 },
  { id: "o8", supplierName: "Metro Wholesale", reference: "PO-8", paymentStatus: "paid", totalCents: 50000 },
];

/** The partner list and the shop's orders are their own routes; everything else goes to the fake the test gave. */
function mount(api: Partial<Api>) {
  const inner = api.get as (path: string, query?: unknown) => Promise<unknown>;
  const get = async (path: string, query?: unknown) => {
    if (path === "/credit/partners") return ok(["lendco", "otherco"]);
    if (path === "/shops/s1/purchase-orders") return ok(orders);
    return inner(path, query);
  };
  render(<SessionProvider api={{ ...api, get } as Api} initial={session}><PayLater /></SessionProvider>);
}

describe("PayLater", () => {
  it("explains what is shared and shows empty states", async () => {
    mount({ get: vi.fn(async () => ok(empty)) as never });

    expect(screen.getByText(/We never share what you sold/)).toBeTruthy();
    expect(await screen.findByText("You have not agreed to share your sales with anyone.")).toBeTruthy();
    expect(screen.getByText("Nothing has been shared yet.")).toBeTruthy();
    expect(screen.getByText("You have no approved credit yet.")).toBeTruthy();
  });

  it("gives consent for a partner and months", async () => {
    const post = vi.fn(async () => ok(full.consents[0]));
    mount({ get: vi.fn(async () => ok(empty)) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("You have not agreed to share your sales with anyone.");

    await user.selectOptions(await screen.findByLabelText("Finance partner to share with"), "lendco");
    await user.click(screen.getByRole("button", { name: "I agree to share my sales" }));

    await waitFor(() => expect(screen.getByText("You agreed to share your sales with lendco.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/credit/consents", { personId: "p1", partnerCode: "lendco", months: 6 });
  });

  it("offers the server's partners and only the orders that are still owing", async () => {
    mount({ get: vi.fn(async () => ok(full)) as never });
    await screen.findByText("Metro Wholesale");

    const partners = Array.from((screen.getByLabelText("Finance partner to share with") as HTMLSelectElement).options).map((o) => o.value);
    expect(partners).toEqual(["", "lendco", "otherco"]);
    const choices = Array.from((screen.getByLabelText("Supplier order") as HTMLSelectElement).options).map((o) => o.value);
    expect(choices).toEqual(["", "o9"]);
  });

  it("shows limit, available, repayments and the share log, and withdraws consent", async () => {
    const del = vi.fn(async () => ok(undefined));
    mount({ get: vi.fn(async () => ok(full)) as never, delete: del as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Metro Wholesale")).toBeTruthy();
    expect(screen.getByText(/Total sales for 6 months: R.98.765,00/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Withdraw agreement with lendco" }));

    await waitFor(() => expect(screen.getByText(/Nothing more will be shared/)).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/shops/s1/credit/consents/c1", { personId: "p1" });
  });

  it("applies for credit and pays a supplier order with approved terms", async () => {
    const post = vi.fn(async (path: string) => (path.endsWith("/draws") ? ok(full.draws[0]) : ok(full.applications[0])));
    mount({ get: vi.fn(async () => ok(full)) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Metro Wholesale");

    await user.selectOptions(screen.getByLabelText("Finance partner"), "c1");
    await user.type(screen.getByLabelText("How much credit do you want (rand)?"), "5000");
    await user.click(screen.getByRole("button", { name: "Apply" }));
    await waitFor(() => expect(screen.getByText("Your application was sent.")).toBeTruthy());
    const [path, body] = post.mock.calls[0] as unknown as [string, Record<string, unknown>];
    expect(path).toBe("/shops/s1/credit/applications");
    expect(body).toMatchObject({ personId: "p1", consentId: "c1", requestedCents: 500000 });

    await user.selectOptions(screen.getByLabelText("Approved credit"), "a1");
    await user.selectOptions(screen.getByLabelText("Supplier order"), "o9");
    await user.click(screen.getByRole("button", { name: "Pay the order with credit" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/credit/draws", { personId: "p1", applicationId: "a1", orderId: "o9" }));
  });

  it("shows the server's message and the plan explanation", async () => {
    mount({ get: vi.fn(async () => fail("plan.feature_not_included", "No.")) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("Pay-later terms are part of a paid plan. See Billing to choose one.");
  });

  it("shows a refusal from the server when the limit is exceeded", async () => {
    const post = vi.fn(async () => fail("credit.limit_exceeded", "Only R2500 of your credit is available."));
    mount({ get: vi.fn(async () => ok(full)) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Metro Wholesale");

    await user.selectOptions(screen.getByLabelText("Approved credit"), "a1");
    await user.selectOptions(screen.getByLabelText("Supplier order"), "o9");
    await user.click(screen.getByRole("button", { name: "Pay the order with credit" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Only R2500 of your credit is available.");
  });
});
