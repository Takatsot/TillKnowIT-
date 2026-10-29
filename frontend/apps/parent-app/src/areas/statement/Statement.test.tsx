// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, thandi } from "../../shared/testing";
import { Statement } from "./Statement";

afterEach(cleanup);

const lines = [
  { at: "2026-10-26T08:00:00Z", kind: "topup", description: "Top-up", amountCents: 10000, balanceAfterCents: 10000 },
  { at: "2026-10-27T09:30:00Z", kind: "purchase", description: "Tuckshop purchase", amountCents: -1500, balanceAfterCents: 8500 },
];

function api(extra: Record<string, unknown> = {}, statement: unknown = ok(lines)) {
  const get = vi.fn(async (path: string) => {
    if (path === "/me/children") return ok([thandi]);
    if (path === "/wallets/w1/qr") return ok({ token: "abc.def", expiresAt: new Date(Date.now() + 60_000).toISOString() });
    return statement;
  });
  return { get: get as never, ...extra };
}

describe("Statement", () => {
  it("lists purchases and top-ups, newest first, with the balance after each", async () => {
    mount(<Statement />, api());

    expect(await screen.findByText("Tuckshop purchase")).toBeTruthy();
    const items = screen.getAllByRole("listitem").map((li) => li.textContent ?? "");
    expect(items[0]).toContain("Tuckshop purchase");
    expect(items[0]).toContain("Purchase");
    expect(items[1]).toContain("Top-up");
    expect(screen.getByText(rand("Balance after: R85,00"), { exact: false })).toBeTruthy();
  });

  it("says so when there is nothing to show", async () => {
    mount(<Statement />, api({}, ok([])));

    expect(await screen.findByText("Nothing happened on this wallet in this time.")).toBeTruthy();
  });

  it("shows the server's message when the statement cannot be loaded", async () => {
    mount(<Statement />, api({}, fail("permission.denied", "You do not have access to this wallet.")));

    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this wallet.");
  });

  it("shows the wallet QR code as a big image", async () => {
    mount(<Statement />, api());
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Tuckshop purchase");

    await user.click(screen.getByRole("button", { name: "Show QR code" }));

    const img = await screen.findByAltText("QR code for Thandi Dlamini's wallet");
    expect(img.getAttribute("src")).toMatch(/^data:image\/gif/);
    expect(screen.getByRole("button", { name: "New QR code" })).toBeTruthy();
  });

  it("shows the server's refusal when the wallet is frozen", async () => {
    const get = vi.fn(async (path: string) => (path === "/me/children" ? ok([thandi]) : path.endsWith("/qr") ? fail("wallet.frozen", "This wallet is not active.") : ok([])));
    mount(<Statement />, { get: get as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Show QR code" }));

    expect((await screen.findByRole("alert")).textContent).toBe("This wallet is not active.");
  });

  it("blocks all cards only after the parent confirms", async () => {
    const post = vi.fn(async () => ok(2));
    mount(<Statement />, api({ post }));
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Tuckshop purchase");

    await user.click(screen.getByRole("button", { name: "Block all cards" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, block them" }));

    await waitFor(() => expect(screen.getByText("2 cards were blocked.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/holders/h1/block-cards", undefined, { callerPersonId: "p1" });
  });

  it("adds a card and blocks it again", async () => {
    const post = vi.fn(async (path: string) => (path.endsWith("/tokens") ? ok({ id: "tk1", holderId: "h1", type: "nfc", status: "active" }) : ok(undefined)));
    mount(<Statement />, api({ post }));
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Tuckshop purchase");

    await user.type(screen.getByLabelText("Number on the card"), "04A1B2C3");
    await user.click(screen.getByRole("button", { name: "Add card" }));
    await user.click(await screen.findByRole("button", { name: "Block this card" }));

    await waitFor(() => expect(screen.getByText("The card was blocked.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/holders/h1/tokens", { callerPersonId: "p1", type: "nfc", rawToken: "04A1B2C3" });
    expect(post).toHaveBeenCalledWith("/tokens/tk1/block", undefined, { callerPersonId: "p1" });
  });

  it("shows the server's refusal when a parent may not add a card", async () => {
    const post = vi.fn(async () => fail("permission.denied", "You do not have access to this school."));
    mount(<Statement />, api({ post }));
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Tuckshop purchase");

    await user.type(screen.getByLabelText("Number on the card"), "04A1B2C3");
    await user.click(screen.getByRole("button", { name: "Add card" }));

    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this school.");
  });
});
