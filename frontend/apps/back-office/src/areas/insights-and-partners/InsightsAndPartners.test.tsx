// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { InsightsAndPartners } from "./InsightsAndPartners";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const optIn = (optedIn: boolean) => ({ shopId: "s1", optedIn, changedAt: null, explanation: "We share only anonymous weekly totals, never your shop's name." });
const grant = { id: "g1", clientId: "c1", clientName: "Acme Analytics", scopes: ["sales.daily"], approvedAt: "2026-10-20T08:00:00Z", active: true };

function fakeGet(opt: unknown, grants: unknown) {
  return vi.fn(async (path: string) => (path.endsWith("/insights-opt-in") ? opt : grants) as never);
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><InsightsAndPartners /></SessionProvider>);
}

describe("InsightsAndPartners", () => {
  it("shows the server's explanation and switches the opt-in", async () => {
    const put = vi.fn(async () => ok(optIn(true)));
    mount({ get: fakeGet(ok(optIn(false)), ok([])) as never, put: put as never });
    const user = userEvent.setup();

    expect(await screen.findByText("We share only anonymous weekly totals, never your shop's name.")).toBeTruthy();
    await user.click(screen.getByLabelText("Include my shop in market insights"));

    await waitFor(() => expect(screen.getByText("Your choice was saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/insights-opt-in", { personId: "p1", optedIn: true });
  });

  it("approves a partner with the chosen scopes", async () => {
    const post = vi.fn(async () => ok(grant));
    mount({ get: fakeGet(ok(optIn(false)), ok([])) as never, post: post as never });
    const user = userEvent.setup();
    expect(await screen.findByText("No partner has access to your sales.")).toBeTruthy();

    await user.type(screen.getByLabelText("Partner client id"), "c1");
    expect((screen.getByRole("button", { name: "Approve access" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByLabelText("Monthly sales totals"));
    await user.click(screen.getByRole("button", { name: "Approve access" }));

    await waitFor(() => expect(screen.getByText("Acme Analytics can now read what you approved.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/partner-access", { personId: "p1", clientId: "c1", scopes: ["sales.monthly"] });
  });

  it("lists approved partners and withdraws one", async () => {
    const del = vi.fn(async () => ok(undefined));
    mount({ get: fakeGet(ok(optIn(true)), ok([grant])) as never, delete: del as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Acme Analytics")).toBeTruthy();
    expect(screen.getAllByText("Daily sales totals").length).toBe(2);
    await user.click(screen.getByRole("button", { name: "Withdraw access for Acme Analytics" }));

    await waitFor(() => expect(screen.getByText("Access for Acme Analytics was withdrawn.")).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/shops/s1/partner-access/g1", { personId: "p1" });
  });

  it("shows the plan explanation and server errors", async () => {
    const post = vi.fn(async () => fail("client.not_found", "That partner is not registered."));
    mount({ get: fakeGet(fail("plan.feature_not_included", "No."), ok([])) as never, post: post as never });
    const user = userEvent.setup();

    expect((await screen.findByRole("alert")).textContent).toBe("This is part of a paid plan. See Billing to choose one.");
    await user.type(screen.getByLabelText("Partner client id"), "zzz");
    await user.click(screen.getByLabelText("Daily sales totals"));
    await user.click(screen.getByRole("button", { name: "Approve access" }));

    await waitFor(() => expect(screen.getAllByRole("alert").some((a) => a.textContent === "That partner is not registered.")).toBe(true));
  });
});
