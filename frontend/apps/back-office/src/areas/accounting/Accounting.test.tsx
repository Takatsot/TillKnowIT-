// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Accounting, ACCOUNT_KEYS } from "./Accounting";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const failedRun = { id: "r1", provider: "xero", day: "2026-10-25", status: "Failed", attempts: 3, error: "Xero said no.", externalReference: null, givenUp: true };

function fakeGet(mappings: unknown, runs: unknown) {
  return vi.fn(async (path: string) => (path.endsWith("/mappings") ? mappings : runs) as never);
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Accounting /></SessionProvider>);
}

describe("Accounting", () => {
  it("says the real sign-in is not built, connects with the code and shows the package", async () => {
    const post = vi.fn(async () => ok({ id: "c1", provider: "sage", tenantName: "Thandi Trading", connected: true, startsOn: "2026-11-01", connectedAt: "2026-10-29T08:00:00Z" }));
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never, post: post as never });
    const user = userEvent.setup();

    expect(screen.getByText(/not built into this screen yet/)).toBeTruthy();
    await user.selectOptions(screen.getByLabelText("Accounting package"), "sage");
    await user.type(screen.getByLabelText("Authorisation code"), "abc123");
    await user.click(screen.getByRole("button", { name: "Connect" }));

    await waitFor(() => expect(screen.getByText("Connected to Sage (Thandi Trading).")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/accounting/connections/sage", { personId: "p1", authorisationCode: "abc123", startsOn: null });
  });

  it("shows all nine account keys, what is missing, and saves the filled ones", async () => {
    const put = vi.fn(async () => ok({ mappings: [], missing: [] }));
    mount({ get: fakeGet(ok({ mappings: [{ key: "cash", accountCode: "1000" }], missing: ["sales", "stock"] }), ok([])) as never, put: put as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Still needed: Sales (excluding VAT), Stock.")).toBeTruthy();
    expect(ACCOUNT_KEYS).toHaveLength(9);
    await waitFor(() => expect((screen.getByLabelText("Cash takings") as HTMLInputElement).value).toBe("1000"));
    await user.type(screen.getByLabelText("Sales (excluding VAT)"), "4000");
    await user.click(screen.getByRole("button", { name: "Save accounts" }));

    await waitFor(() => expect(screen.getByText("The accounts were saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/accounting/mappings", { personId: "p1", mappings: [{ key: "cash", accountCode: "1000" }, { key: "sales", accountCode: "4000" }] });
  });

  it("lists runs with their error and retries a failed one", async () => {
    const post = vi.fn(async () => ok({ ...failedRun, status: "Succeeded" }));
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([failedRun])) as never, post: post as never });
    const user = userEvent.setup();

    expect(await screen.findByText(/Xero said no\./)).toBeTruthy();
    expect(screen.getByText("We stopped trying. Fix the problem, then retry.")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Retry 2026-10-25" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/accounting/export-runs/r1/retry", { personId: "p1" }));
    expect(await screen.findByText("2026-10-25 was sent again.")).toBeTruthy();
  });

  it("explains the plan when export is not included, and shows the empty state", async () => {
    const refused = fail("plan.feature_not_included", "Not in your plan.");
    mount({ get: fakeGet(refused, refused) as never });

    const alerts = await screen.findAllByRole("alert");
    expect(alerts[0]!.textContent).toContain("part of a paid plan");
  });

  it("shows the empty run list and the server's message when connecting fails", async () => {
    const post = vi.fn(async () => fail("accounting.code_invalid", "That code did not work."));
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never, post: post as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Nothing has been sent yet.")).toBeTruthy();
    await user.type(screen.getByLabelText("Authorisation code"), "bad");
    await user.click(screen.getByRole("button", { name: "Connect" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That code did not work.");
  });
});
