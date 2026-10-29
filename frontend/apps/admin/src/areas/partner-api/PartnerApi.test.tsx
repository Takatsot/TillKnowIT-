// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PartnerApi } from "./PartnerApi";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const bank = { id: "c1", name: "Bank of Spaza", keyPrefix: "tk_live_abcd", scopes: ["sales.daily"], rateLimitPerMinute: 60, revoked: false };
const gone = { id: "c2", name: "Old Partner", keyPrefix: "tk_live_zzzz", scopes: ["sales.monthly"], rateLimitPerMinute: 10, revoked: true };
const newKey = { clientId: "c1", name: "Bank of Spaza", keyPrefix: "tk_live_abcd", key: "tk_live_abcdSECRETSECRET", scopes: ["sales.daily"], rateLimitPerMinute: 60 };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><PartnerApi /></SessionProvider>);
}

describe("PartnerApi", () => {
  it("lists clients with prefix, scopes and status but never a key", async () => {
    mount({ get: vi.fn(async () => ok([bank, gone])) as never });

    expect(await screen.findByText("Bank of Spaza")).toBeTruthy();
    expect(screen.getByText("tk_live_abcd")).toBeTruthy();
    expect(screen.getByText("Revoked")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Rotate key Old Partner/ })).toBeNull();
  });

  it("registers a partner and shows the key once with a warning and a copy button", async () => {
    const post = vi.fn(async () => ok(newKey));
    const get = vi.fn(async () => ok([]));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    expect(await screen.findByText("There are no API clients yet.")).toBeTruthy();

    await user.type(screen.getByLabelText("Partner name"), "Bank of Spaza");
    await user.click(screen.getByLabelText("Daily sales (sales.daily)"));
    await user.clear(screen.getByLabelText(/Rate limit/));
    await user.type(screen.getByLabelText(/Rate limit/), "120");
    await user.click(screen.getByRole("button", { name: "Register partner" }));

    expect(await screen.findByText(/shown once and cannot be shown again/)).toBeTruthy();
    expect((screen.getByLabelText("API key") as HTMLInputElement).value).toBe("tk_live_abcdSECRETSECRET");
    expect(post).toHaveBeenCalledWith("/admin/api-clients", { callerPersonId: "p1", name: "Bank of Spaza", contactEmail: null, scopes: ["sales.daily"], rateLimitPerMinute: 120 });

    await user.click(screen.getByRole("button", { name: "Copy key" }));
    await waitFor(() => expect(screen.getByText("The key was copied.")).toBeTruthy());
    expect(writeText).toHaveBeenCalledWith("tk_live_abcdSECRETSECRET");

    await user.click(screen.getByRole("button", { name: "I have copied the key" }));
    expect(screen.queryByLabelText("API key")).toBeNull();
  });

  it("needs a scope and a rate limit in range before it asks the server", async () => {
    const post = vi.fn(async () => ok(newKey));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("There are no API clients yet.");

    await user.type(screen.getByLabelText("Partner name"), "X");
    await user.clear(screen.getByLabelText(/Rate limit/));
    await user.type(screen.getByLabelText(/Rate limit/), "9000");
    await user.click(screen.getByRole("button", { name: "Register partner" }));

    expect(screen.getByText("The rate limit is a whole number from 1 to 6000.")).toBeTruthy();
    expect(screen.getByText("Choose at least one thing the partner may read.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();
  });

  it("rotates a key and shows the new key once with the overlap note", async () => {
    const post = vi.fn(async () => ok(newKey));
    mount({ get: vi.fn(async () => ok([bank])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Bank of Spaza");

    await user.click(screen.getByRole("button", { name: "Rotate key Bank of Spaza" }));

    expect(await screen.findByText(/keeps working for 24 hours/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/admin/api-clients/c1/rotate-key", undefined, { callerPersonId: "p1" });
  });

  it("asks twice before revoking and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("client.not_found", "Partner not found."));
    mount({ get: vi.fn(async () => ok([bank])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Bank of Spaza");

    await user.click(screen.getByRole("button", { name: "Revoke Bank of Spaza" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Revoke Bank of Spaza for good" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Partner not found.");
    expect(post).toHaveBeenCalledWith("/admin/api-clients/c1/revoke", undefined, { callerPersonId: "p1" });
  });

  it("shows the server's message when the list cannot be loaded", async () => {
    mount({ get: vi.fn(async () => fail("permission.denied", "Only a platform admin can see partners.")) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("Only a platform admin can see partners.");
  });
});
