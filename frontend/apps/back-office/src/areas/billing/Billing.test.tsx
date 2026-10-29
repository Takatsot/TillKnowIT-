// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Billing } from "./Billing";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const plans = [
  { id: "a", code: "shop_free", audience: "shop", name: "Free", priceCents: 0, isActive: true, features: [] },
  { id: "b", code: "shop_pro", audience: "shop", name: "Pro", priceCents: 29900, isActive: true, features: [{ feature: "multi_branch", limit: null }, { feature: "messages", limit: 500 }] },
];
const sub = (over: object = {}) => ({
  id: "x", organisationId: "s1", planCode: "shop_free", status: "Active", periodStart: "2026-10-01T00:00:00Z", periodEnd: "2026-11-01T00:00:00Z",
  pendingPlanCode: null, cancelAtPeriodEnd: false, invoiceId: null, chargedCents: 0, ...over,
});
const failed = { id: "i1", number: "INV-001", status: "Failed", subtotalCents: 26000, vatPercent: 15, vatCents: 3900, totalCents: 29900, dueAt: "2026-10-05T00:00:00Z", paidAt: null,
  lines: [{ description: "Pro plan, October", metric: null, quantity: 1, unitCents: 26000, amountCents: 26000 }] };

function fakeGet(subscription: unknown, invoices: unknown[] = []) {
  return vi.fn(async (path: string) => {
    if (path === "/plans") return ok(plans);
    if (path.endsWith("/subscription")) return subscription as never;
    return ok(invoices);
  });
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Billing /></SessionProvider>);
}

describe("Billing", () => {
  it("subscribes when there is no subscription yet", async () => {
    const post = vi.fn(async () => ok(sub({ planCode: "shop_pro" })));
    const get = fakeGet(fail("subscription.not_found", "This business has no subscription."));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();

    expect(await screen.findByText("You are on the free plan. Choose a plan below to add more features.")).toBeTruthy();
    await user.click(await screen.findByRole("button", { name: "Subscribe to Pro" }));

    await waitFor(() => expect(screen.getByText("You are now on Pro.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/subscriptions", { personId: "p1", organisationId: "s1", planCode: "shop_pro" });
    expect(get).toHaveBeenCalledWith("/plans", { audience: "shop" });
  });

  it("upgrades and cancels with the change route", async () => {
    const patch = vi.fn(async () => ok(sub({ planCode: "shop_pro" })));
    mount({ get: fakeGet(ok(sub())) as never, patch: patch as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Upgrade to Pro" }));
    await waitFor(() => expect(screen.getByText("Your plan was changed.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/organisations/s1/subscription", { personId: "p1", planCode: "shop_pro", cancel: false });

    await user.click(screen.getByRole("button", { name: "Cancel plan" }));
    await waitFor(() => expect(patch).toHaveBeenLastCalledWith("/organisations/s1/subscription", { personId: "p1", planCode: null, cancel: true }));
  });

  it("shows a past-due banner that says the till is not affected, and pays the failed invoice", async () => {
    const post = vi.fn(async () => ok({ ...failed, status: "Paid" }));
    mount({ get: fakeGet(ok(sub({ planCode: "shop_pro", status: "PastDue" })), [failed]) as never, post: post as never });
    const user = userEvent.setup();

    const banner = await screen.findByRole("status");
    expect(banner.textContent).toContain("Selling at the till is never affected");
    await user.click(await screen.findByRole("button", { name: "Pay INV-001" }));

    await waitFor(() => expect(screen.getByText("Invoice INV-001 was paid.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/invoices/i1/pay", undefined, { personId: "p1", audience: "shop" });
  });

  it("explains a limited subscription and shows invoice lines with VAT", async () => {
    mount({ get: fakeGet(ok(sub({ planCode: "shop_pro", status: "Limited" })), [failed]) as never });
    const user = userEvent.setup();

    expect((await screen.findAllByRole("alert"))[0]!.textContent).toContain("paid features are paused");
    await user.click(await screen.findByRole("button", { name: "View INV-001" }));

    expect(screen.getByText("Pro plan, October")).toBeTruthy();
    expect(screen.getByText(/VAT \(15%\)/)).toBeTruthy();
  });

  it("shows the server's refusal and the empty invoice state", async () => {
    const patch = vi.fn(async () => fail("payment.failed", "Your card was declined."));
    mount({ get: fakeGet(ok(sub())) as never, patch: patch as never });
    const user = userEvent.setup();

    expect(await screen.findByText("No invoices yet.")).toBeTruthy();
    await user.click(await screen.findByRole("button", { name: "Upgrade to Pro" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Your card was declined.");
  });
});
