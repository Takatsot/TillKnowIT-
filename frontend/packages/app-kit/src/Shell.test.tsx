// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defineSection } from "./section";
import { isSignedIn, loadSession, SessionProvider, useApi, useSession, type Session } from "./session";
import { Shell } from "./Shell";

beforeEach(() => localStorage.clear());
afterEach(cleanup);

const future = new Date(Date.now() + 3600_000).toISOString();
const answer = { accessToken: "tok", expiresAt: future, personId: "p1", displayName: "Thandi Nkosi", language: "en" };
const signedIn: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "d1", language: "en", token: "tok", expiresAt: future, displayName: "Thandi Nkosi" };
const signedOut: Session = { ...signedIn, token: "", personId: "" };
const memberships = [
  { membershipId: "m1", organisationId: "shop1", organisationName: "Thandi's Spaza", organisationType: "Shop", roleCode: "owner" },
  { membershipId: "m2", organisationId: "sup1", organisationName: "Fresh Foods", organisationType: "Supplier", roleCode: "admin" },
  { membershipId: "m3", organisationId: "shop2", organisationName: "Soweto branch", organisationType: "Shop", roleCode: "manager" },
];

function Probe() {
  const s = useSession();
  const api = useApi();
  return (
    <div>
      <p>{`shop=${s.shopId}|supplier=${s.supplierOrganisationId}|person=${s.personId}`}</p>
      <button onClick={() => void api.get("/something")}>call</button>
    </div>
  );
}
const section = defineSection({ id: "probe", order: 1, title: "t", messages: { en: { t: "Probe" } }, component: Probe });

function mount(api: Partial<Api>, initial?: Session) {
  render(<SessionProvider api={api as Api} initial={initial}><Shell title="Test" sections={[section]} /></SessionProvider>);
}

describe("Shell sign-in", () => {
  it("shows only the sign-in when nobody is signed in", () => {
    mount({}, signedOut);

    expect(screen.getByRole("button", { name: "Send me a code" })).toBeTruthy();
    expect(screen.queryByText("call")).toBeNull();
  });

  it("signs in with a phone and a code, shows the dev code, then the person's name and organisation choice", async () => {
    const post = vi.fn(async (path: string) => (path === "/auth/otp/request" ? ok({ message: "sent", devCode: "123456" }) : ok(answer)));
    const get = vi.fn(async () => ok(memberships));
    mount({ post: post as never, get: get as never }, signedOut);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Your cell phone number"), "0821234567");
    await user.click(screen.getByRole("button", { name: "Send me a code" }));
    expect(await screen.findByText("Development only")).toBeTruthy();
    expect(screen.getByText(/123456/)).toBeTruthy();
    await user.type(screen.getByLabelText("The code we sent you"), "123456");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(post).toHaveBeenCalledWith("/auth/otp/request", { phone: "0821234567" });
    expect(post).toHaveBeenCalledWith("/auth/otp/verify", { phone: "0821234567", code: "123456" });
    expect((await screen.findAllByText("Thandi Nkosi")).length).toBeGreaterThan(0);
    expect(get).toHaveBeenCalledWith("/people/p1/memberships");
    await screen.findByText("shop=shop1|supplier=sup1|person=p1");
    expect(JSON.parse(localStorage.getItem("tillknowit.session") ?? "{}")).toMatchObject({ token: "tok", personId: "p1", displayName: "Thandi Nkosi" });
  });

  it("does not show a development notice when the server sends no dev code", async () => {
    mount({ post: vi.fn(async () => ok({ message: "sent" })) as never }, signedOut);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Your cell phone number"), "0821234567");
    await user.click(screen.getByRole("button", { name: "Send me a code" }));

    await screen.findByLabelText("The code we sent you");
    expect(screen.queryByText("Development only")).toBeNull();
  });

  it.each([
    ["otp.phone_invalid", "That does not look like a cell phone number. Check it and try again."],
    ["otp.too_many", "Too many tries. Please wait a few minutes and try again."],
  ])("says %s in plain words", async (code, words) => {
    mount({ post: vi.fn(async () => fail(code, "raw server text")) as never }, signedOut);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Your cell phone number"), "123");
    await user.click(screen.getByRole("button", { name: "Send me a code" }));

    expect((await screen.findByRole("alert")).textContent).toBe(words);
  });

  it("says a wrong code in plain words and stays on the code step", async () => {
    const post = vi.fn(async (path: string) => (path === "/auth/otp/request" ? ok({ message: "sent" }) : fail("otp.invalid", "raw")));
    mount({ post: post as never }, signedOut);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Your cell phone number"), "0821234567");
    await user.click(screen.getByRole("button", { name: "Send me a code" }));

    await user.type(await screen.findByLabelText("The code we sent you"), "000000");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That code is not right, or it has expired. Check it or ask for a new code.");
  });

  it("signs out with the button and clears what was stored", async () => {
    mount({ get: vi.fn(async () => ok(memberships)) as never }, signedIn);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("button", { name: "Send me a code" })).toBeTruthy();
    expect(JSON.parse(localStorage.getItem("tillknowit.session") ?? "{}")).toMatchObject({ token: "", personId: "", displayName: "" });
  });

  it("lets the person switch organisation, which sets the shop id", async () => {
    mount({ get: vi.fn(async () => ok(memberships)) as never }, signedIn);
    const user = userEvent.setup();
    await screen.findByText("shop=shop1|supplier=sup1|person=p1");

    await user.selectOptions(screen.getByLabelText("Who are you working for?", { selector: "select" }), "shop2");

    await screen.findByText("shop=shop2|supplier=sup1|person=p1");
  });

  it("signs the person out with a message when any call answers auth.required", async () => {
    const get = vi.fn(async (path: string) => (path.endsWith("/memberships") ? ok(memberships) : fail("auth.required", "Sign in to do this.")));
    mount({ get: get as never }, signedIn);
    const user = userEvent.setup();
    await screen.findByText("shop=shop1|supplier=sup1|person=p1");

    await user.click(screen.getByRole("button", { name: "call" }));

    await waitFor(() => expect(screen.getByText("Please sign in again.")).toBeTruthy());
    expect(screen.queryByText("call")).toBeNull();
  });

  it("treats an expired stored token as signed out", () => {
    localStorage.setItem("tillknowit.session", JSON.stringify({ ...signedIn, expiresAt: new Date(Date.now() - 1000).toISOString() }));

    const loaded = loadSession();

    expect(isSignedIn(loaded)).toBe(false);
    expect(loaded.token).toBe("");
    expect(loaded.deviceId).toBe("d1");
  });
});
