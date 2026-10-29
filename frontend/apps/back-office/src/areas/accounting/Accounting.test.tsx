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

const notConnected = fail("accounting.not_connected", "No accounting package is connected.");
const existing = { id: "c0", provider: "xero", tenantName: "Thandi Trading", connected: true, startsOn: "2026-09-01", connectedAt: "2026-09-01T08:00:00Z" };

function fakeGet(mappings: unknown, runs: unknown, connection: unknown = notConnected) {
  return vi.fn(async (path: string) => (path.endsWith("/connection") ? connection : path.endsWith("/mappings") ? mappings : runs) as never);
}

function mount(api: Partial<Api>, props: { goTo?: (url: string) => void; search?: string } = {}) {
  render(<SessionProvider api={api as Api} initial={session}><Accounting search="" {...props} /></SessionProvider>);
}

describe("Accounting", () => {
  it("connects with a pasted code and shows the package", async () => {
    const post = vi.fn(async () => ok({ id: "c1", provider: "sage", tenantName: "Thandi Trading", connected: true, startsOn: "2026-11-01", connectedAt: "2026-10-29T08:00:00Z" }));
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never, post: post as never });
    const user = userEvent.setup();

    await user.selectOptions(screen.getByLabelText("Accounting package"), "sage");
    await user.type(screen.getByLabelText("Authorisation code"), "abc123");
    await user.click(screen.getByRole("button", { name: "Connect" }));

    await waitFor(() => expect(screen.getByText("Connected to Sage (Thandi Trading).")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/accounting/connections/sage", { personId: "p1", authorisationCode: "abc123", startsOn: null });
  });

  it("sends the browser to the package's own sign-in page, with the first day if one was chosen", async () => {
    const get = vi.fn(async (path: string) => (path.endsWith("/authorise") ? ok({ url: "https://login.xero.com/identity/connect/authorize?state=abc" }) : fakeGet(ok({ mappings: [], missing: [] }), ok([]))(path)));
    const goTo = vi.fn();
    mount({ get: get as never }, { goTo });
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("First day to send (optional)"), "2026-11-01");
    await user.click(screen.getByRole("button", { name: "Sign in with Xero" }));

    await waitFor(() => expect(goTo).toHaveBeenCalledWith("https://login.xero.com/identity/connect/authorize?state=abc"));
    expect(get).toHaveBeenCalledWith("/shops/s1/accounting/connections/xero/authorise", { personId: "p1", startsOn: "2026-11-01" });
  });

  it("shows the server's refusal when the sign-in cannot start, and does not leave the page", async () => {
    const get = vi.fn(async (path: string) => (path.endsWith("/authorise") ? fail("accounting.oauth_unavailable", "Signing in to this package is not set up here yet.") : fakeGet(ok({ mappings: [], missing: [] }), ok([]))(path)));
    const goTo = vi.fn();
    mount({ get: get as never }, { goTo });
    const user = userEvent.setup();

    await user.selectOptions(screen.getByLabelText("Accounting package"), "sage");
    await user.click(screen.getByRole("button", { name: "Sign in with Sage" }));

    expect(await screen.findByText("Signing in to this package is not set up here yet.")).toBeTruthy();
    expect(goTo).not.toHaveBeenCalled();
  });

  it("tells the owner when the package sent them back connected, or failed", async () => {
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never }, { search: "?accounting=connected" });
    expect(await screen.findByText(/You are connected/)).toBeTruthy();
    cleanup();

    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never }, { search: "?accounting=failed&reason=The%20sign-in%20was%20cancelled." });
    expect(await screen.findByText("The sign-in was cancelled.")).toBeTruthy();
  });

  it("loads the existing connection on open and can disconnect it", async () => {
    const del = vi.fn(async () => ok(undefined));
    const get = fakeGet(ok({ mappings: [], missing: [] }), ok([]), ok(existing));
    mount({ get: get as never, delete: del as never });
    const user = userEvent.setup();

    expect(await screen.findByText(/Connected to Xero \(Thandi Trading\)\. Connect again below to replace it\./)).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/shops/s1/accounting/connection", { personId: "p1" });
    await user.click(screen.getByRole("button", { name: "Disconnect" }));
    await waitFor(() => expect(screen.getByText("The accounting package was disconnected.")).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/shops/s1/accounting/connection", { personId: "p1" });
    expect(screen.queryByRole("button", { name: "Disconnect" })).toBeNull();
  });

  it("shows the connect form without an error when nothing is connected yet", async () => {
    mount({ get: fakeGet(ok({ mappings: [], missing: [] }), ok([])) as never });
    expect(await screen.findByRole("button", { name: "Connect" })).toBeTruthy();
    await waitFor(() => expect(screen.queryByText("Checking for an existing connection...")).toBeNull());
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("button", { name: "Disconnect" })).toBeNull();
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
