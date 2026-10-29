// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Plan } from "./Plan";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "org1", deviceId: "", language: "en" };
const plans = [
  { id: "pl1", code: "supplier_free", name: "Free", priceCents: 0, features: [] },
  { id: "pl2", code: "supplier_plus", name: "Plus", priceCents: 29900, features: [{ feature: "insight_reports", limit: null }] },
];
const sub = { planCode: "supplier_free", status: "active", periodStart: "2026-10-01T00:00:00Z", periodEnd: "2026-11-01T00:00:00Z", pendingPlanCode: null, cancelAtPeriodEnd: false };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Plan /></SessionProvider>);
  return userEvent.setup({ delay: null });
}

const getter = (subscription: () => unknown) => vi.fn(async (path: string) => (path === "/plans" ? ok(plans) : subscription()) as never);

describe("Plan", () => {
  it("shows supplier plans and the current subscription", async () => {
    const get = getter(() => ok(sub));
    mount({ get: get as never });

    expect(await screen.findByText("Plan Free, status active. Current period 2026-10-01 to 2026-11-01.")).toBeTruthy();
    expect(screen.getByText("Market trend reports")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/plans", { audience: "supplier" });
    expect(get).toHaveBeenCalledWith("/organisations/org1/subscription", { personId: "p1", audience: "supplier" });
  });

  it("subscribes when there is none yet", async () => {
    const post = vi.fn(async () => ok(sub));
    const user = mount({ get: getter(() => fail("subscription.not_found", "This business has no subscription.")) as never, post: post as never });

    expect(await screen.findByText(/no subscription yet/)).toBeTruthy();
    await user.click(await screen.findByRole("button", { name: "Choose Plus" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/subscriptions", { personId: "p1", organisationId: "org1", planCode: "supplier_plus" }));
  });

  it("changes plan and shows a refusal as the server words it", async () => {
    const patch = vi.fn(async () => fail("payment.failed", "The payment did not go through."));
    const user = mount({ get: getter(() => ok(sub)) as never, patch: patch as never });

    await user.click(await screen.findByRole("button", { name: "Choose Plus" }));

    expect((await screen.findByText("The payment did not go through.")).getAttribute("role")).toBe("alert");
    expect(patch).toHaveBeenCalledWith("/organisations/org1/subscription", { personId: "p1", planCode: "supplier_plus", cancel: false });
  });

  it("shows an error when plans cannot be loaded", async () => {
    mount({ get: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) as never });
    expect((await screen.findAllByText("We cannot reach the server.")).length).toBeGreaterThan(0);
  });
});
