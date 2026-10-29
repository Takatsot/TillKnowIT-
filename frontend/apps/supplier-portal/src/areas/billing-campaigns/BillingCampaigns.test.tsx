// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount } from "../../shared/testing";
import { BillingCampaigns } from "./BillingCampaigns";

afterEach(cleanup);

const plans = [
  { id: "pl1", code: "supplier_free", audience: "supplier", name: "Free", priceCents: 0, isActive: true, features: [] },
  { id: "pl3", code: "supplier_premium", audience: "supplier", name: "Premium", priceCents: 99900, isActive: true, features: [{ feature: "sponsored", limit: null }] },
];
const sub = { planCode: "supplier_free", status: "active", periodStart: "2026-10-01T00:00:00Z", periodEnd: "2026-11-01T00:00:00Z", pendingPlanCode: null, cancelAtPeriodEnd: false };
const invoice = { id: "i1", number: "INV-1", status: "failed", totalCents: 114885, dueAt: "2026-11-01T00:00:00Z", paidAt: null, lines: [] };
const campaign = { id: "c1", name: "Maize push", status: "active", budgetCents: 50000, spentCents: 1200, areas: ["JHB"], startsOn: "2026-11-01", endsOn: "2026-11-30", sponsored: true };

function getter(over: Record<string, () => unknown> = {}) {
  return vi.fn(async (path: string) => {
    for (const [suffix, make] of Object.entries(over)) if (path.endsWith(suffix)) return make() as never;
    if (path === "/plans") return ok(plans);
    if (path.endsWith("/subscription")) return ok(sub);
    if (path.endsWith("/invoices")) return ok([invoice]);
    if (path.endsWith("/sponsored-campaigns")) return ok([campaign]);
    return ok(undefined);
  });
}

describe("Plan and invoices", () => {
  it("shows plans for suppliers, the subscription and invoices", async () => {
    const get = getter();
    mount(<BillingCampaigns />, { get: get as never });

    expect(await screen.findByText("Plan Free, status active. Current period 2026-10-01 to 2026-11-01.")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/plans", { audience: "supplier" });
    expect(get).toHaveBeenCalledWith("/organisations/org1/subscription", { personId: "p1", audience: "supplier" });
    expect(await screen.findByText("INV-1")).toBeTruthy();
    expect(screen.getByRole("cell", { name: "Sponsored campaigns" })).toBeTruthy();
  });

  it("changes plan on an existing subscription", async () => {
    const patch = vi.fn(async () => ok(sub));
    mount(<BillingCampaigns />, { get: getter() as never, patch: patch as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Choose Premium" }));

    await waitFor(() => expect(screen.getByText("You are now on Premium.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/organisations/org1/subscription", { personId: "p1", planCode: "supplier_premium", cancel: false });
  });

  it("subscribes when there is no subscription yet", async () => {
    const post = vi.fn(async () => ok(sub));
    mount(<BillingCampaigns />, { get: getter({ "/subscription": () => fail("subscription.not_found", "This business has no subscription.") }) as never, post: post as never });
    const user = userEvent.setup({ delay: null });

    expect(await screen.findByText(/no subscription yet, so you are on the free plan/)).toBeTruthy();
    await user.click(await screen.findByRole("button", { name: "Choose Premium" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/subscriptions", { personId: "p1", organisationId: "org1", planCode: "supplier_premium" }));
  });

  it("pays a failed invoice and shows a refusal", async () => {
    const post = vi.fn(async () => fail("payment.failed", "The payment did not go through."));
    mount(<BillingCampaigns />, { get: getter() as never, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Pay INV-1" }));

    expect((await screen.findByText("The payment did not go through.")).getAttribute("role")).toBe("alert");
    expect(post).toHaveBeenCalledWith("/invoices/i1/pay", undefined, { personId: "p1", audience: "supplier" });
  });
});

describe("Sponsored campaigns", () => {
  async function openCampaigns(api: Parameters<typeof mount>[1]) {
    mount(<BillingCampaigns />, api);
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Sponsored campaigns" }));
    return user;
  }

  it("explains Premium, billing by use and the label, and lists campaigns", async () => {
    await openCampaigns({ get: getter() as never });
    expect(await screen.findByText("Maize push")).toBeTruthy();
    expect(screen.getByText(/Premium plan/)).toBeTruthy();
    expect(screen.getByText(/billed by use/)).toBeTruthy();
    expect(screen.getAllByText(/Sponsored/).length).toBeGreaterThan(1);
  });

  it("says when there are no campaigns", async () => {
    await openCampaigns({ get: getter({ "/sponsored-campaigns": () => ok([]) }) as never });
    expect(await screen.findByText("No campaigns yet.")).toBeTruthy();
  });

  it("pauses a campaign and shows its usage", async () => {
    const post = vi.fn(async () => ok({ ...campaign, status: "paused" }));
    const get = getter({ "/usage": () => ok({ campaignId: "c1", showings: 24, spentCents: 1200, remainingCents: 48800 }) });
    const user = await openCampaigns({ get: get as never, post: post as never });

    await user.click(await screen.findByRole("button", { name: "Pause Maize push" }));
    await waitFor(() => expect(screen.getByText("Maize push was paused.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/sponsored-campaigns/c1/pause", undefined, { personId: "p1", paused: true });

    await user.click(screen.getByRole("button", { name: "Usage of Maize push" }));
    expect(await screen.findByText(/24 showings so far/)).toBeTruthy();
  });

  it("creates a campaign in cents", async () => {
    const post = vi.fn(async () => ok(campaign));
    const user = await openCampaigns({ get: getter() as never, post: post as never });
    await screen.findByText("Maize push");

    await user.type(screen.getByLabelText("Campaign name"), "Rice push");
    await user.type(screen.getByLabelText("Budget (rand)"), "500");
    await user.type(screen.getByLabelText("Price per showing (rand)"), "0.50");
    await user.type(screen.getByLabelText("Areas (municipality codes, separated by commas)"), "JHB");
    await user.type(screen.getByLabelText("Starts on"), "2026-11-01");
    await user.type(screen.getByLabelText("Ends on"), "2026-11-30");
    await user.click(screen.getByRole("button", { name: "Start campaign" }));

    await waitFor(() => expect(screen.getByText("Maize push was started.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/sponsored-campaigns", {
      personId: "p1", supplierOrganisationId: "org1", name: "Rice push", offerId: null, budgetCents: 50000, showingCents: 50, areas: ["JHB"], startsOn: "2026-11-01", endsOn: "2026-11-30",
    });
  });

  it("explains the plan when sponsored is not included", async () => {
    const post = vi.fn(async () => fail("plan.feature_not_included", "Sponsored placements are not part of your plan."));
    const user = await openCampaigns({ get: getter() as never, post: post as never });
    await screen.findByText("Maize push");

    await user.type(screen.getByLabelText("Campaign name"), "Rice push");
    await user.type(screen.getByLabelText("Budget (rand)"), "500");
    await user.type(screen.getByLabelText("Price per showing (rand)"), "0.50");
    await user.type(screen.getByLabelText("Areas (municipality codes, separated by commas)"), "JHB");
    await user.type(screen.getByLabelText("Starts on"), "2026-11-01");
    await user.type(screen.getByLabelText("Ends on"), "2026-11-30");
    await user.click(screen.getByRole("button", { name: "Start campaign" }));

    expect(await screen.findByText(/need the Premium plan/)).toBeTruthy();
  });
});
