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

const cards = [
  { id: "tk1", holderId: "h1", type: "nfc", status: "active" },
  { id: "tk2", holderId: "h1", type: "nfc", status: "blocked" },
];

function api(extra: Record<string, unknown> = {}, statement: unknown = ok(lines), tokens: unknown = ok(cards)) {
  const get = vi.fn(async (path: string) => {
    if (path === "/me/children") return ok([thandi]);
    if (path === "/holders/h1/tokens") return tokens;
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

  it("lists the child's cards and blocks a lost one from the list", async () => {
    const post = vi.fn(async () => ok(undefined));
    const m = api({ post });
    mount(<Statement />, m);
    const user = userEvent.setup({ delay: null });

    expect(await screen.findByText("Card 1 (tap card)")).toBeTruthy();
    expect(screen.getByText("Card 2 (tap card)")).toBeTruthy();
    expect(screen.getByText("Working")).toBeTruthy();
    expect(screen.getByText("Blocked")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Block card 2" })).toBeNull();
    expect(m.get).toHaveBeenCalledWith("/holders/h1/tokens", { callerPersonId: "p1" });

    await user.click(screen.getByRole("button", { name: "Block card 1" }));

    await waitFor(() => expect(screen.getByText("The card was blocked.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/tokens/tk1/block", undefined, { callerPersonId: "p1" });
    expect((m.get as unknown as { mock: { calls: unknown[][] } }).mock.calls.filter((c) => c[0] === "/holders/h1/tokens")).toHaveLength(2);
  });

  it("says so when no card was issued, and explains the school issues cards", async () => {
    mount(<Statement />, api({}, ok(lines), ok([])));

    expect(await screen.findByText("No card or wristband has been issued for this child yet.")).toBeTruthy();
    expect(screen.getByText(/issued by the school/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Add card" })).toBeNull();
  });

  it("shows the server's refusal when the cards cannot be listed or blocked", async () => {
    mount(<Statement />, api({ post: vi.fn(async () => fail("permission.denied", "You do not have access to this card.")) }));
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Block card 1" }));
    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this card.");
    cleanup();

    mount(<Statement />, api({}, ok(lines), fail("holder.not_found", "Child not found.")));
    expect((await screen.findByRole("alert")).textContent).toBe("Child not found.");
  });
});
