// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BuyingGroups } from "./BuyingGroups";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const mine = { id: "g1", name: "Soweto Stokvel", kind: "Stokvel", adminShopId: "s1", members: 3, youAreAdmin: true };
const other = { id: "g2", name: "Hillcrest School", kind: "School", adminShopId: "s9", members: 5, youAreAdmin: false };
const history = {
  groupId: "g1", name: "Soweto Stokvel",
  deals: [{ id: "d1", supplierName: "Metro", productName: "Maize meal 10kg", totalCases: 12, minTotalCases: 20, closesAt: "2026-11-10T10:00:00Z", status: "Open", yourCases: 4 }],
  members: [{ shopId: "s1", shopName: "Thandi's Spaza", joinedAt: "2026-09-01T08:00:00Z", casesOnOpenDeals: 4 }, { shopId: "s2", shopName: "Alex Tuck", joinedAt: "2026-09-05T08:00:00Z", casesOnOpenDeals: 8 }],
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><BuyingGroups /></SessionProvider>);
}

describe("BuyingGroups", () => {
  it("lists groups with the role, and the empty state", async () => {
    mount({ get: vi.fn(async () => ok([mine, other])) as never });
    expect(await screen.findByText("Soweto Stokvel")).toBeTruthy();
    expect(screen.getByText("Group admin")).toBeTruthy();
    expect(screen.getByText("Member")).toBeTruthy();
    cleanup();

    mount({ get: vi.fn(async () => ok([])) as never });
    expect(await screen.findByText("Your shop is not in a buying group yet.")).toBeTruthy();
  });

  it("creates a group", async () => {
    const get = vi.fn(async () => ok([]));
    const post = vi.fn(async () => ok(mine));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Your shop is not in a buying group yet.");

    await user.type(screen.getByLabelText("Group name"), "Soweto Stokvel");
    await user.click(screen.getByRole("button", { name: "Create group" }));

    await waitFor(() => expect(screen.getByText("Soweto Stokvel was created.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/buying-groups", { callerPersonId: "p1", shopId: "s1", name: "Soweto Stokvel", kind: "Stokvel" });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("shows the invite code once with its rules", async () => {
    const post = vi.fn(async () => ok({ groupId: "g1", code: "K7Q2-91XM", expiresAt: "2026-11-05T10:00:00Z" }));
    mount({ get: vi.fn(async () => ok([mine])) as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Invite a shop to Soweto Stokvel" }));

    expect(await screen.findByText("Code: K7Q2-91XM")).toBeTruthy();
    expect(screen.getByText(/shown only now/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/buying-groups/g1/invites", undefined, { callerPersonId: "p1" });
  });

  it("joins with a code and shows the server's refusal for a bad one", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(fail("invite.invalid", "That code is not valid or has expired."))
      .mockResolvedValueOnce(ok(other));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Your shop is not in a buying group yet.");

    await user.type(screen.getByLabelText("Invite code"), "BAD");
    await user.click(screen.getByRole("button", { name: "Join group" }));
    expect((await screen.findByRole("alert")).textContent).toBe("That code is not valid or has expired.");

    await user.clear(screen.getByLabelText("Invite code"));
    await user.type(screen.getByLabelText("Invite code"), "GOOD");
    await user.click(screen.getByRole("button", { name: "Join group" }));
    await waitFor(() => expect(screen.getByText("You joined Hillcrest School.")).toBeTruthy());
    expect(post).toHaveBeenLastCalledWith("/shops/s1/buying-groups/join", { callerPersonId: "p1", code: "GOOD" });
  });

  it("leaves a group", async () => {
    const post = vi.fn(async () => ok(undefined));
    mount({ get: vi.fn(async () => ok([other])) as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Leave Hillcrest School" }));

    await waitFor(() => expect(screen.getByText("You left Hillcrest School.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/shops/s1/buying-groups/g2/leave", undefined, { callerPersonId: "p1" });
  });

  it("shows history and members to the admin, and removes a member", async () => {
    const del = vi.fn(async () => ok(undefined));
    const get = vi.fn(async (path: string) => (path.endsWith("/history") ? ok(history) : ok([mine])));
    mount({ get: get as never, delete: del as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "History of Soweto Stokvel" }));
    expect(await screen.findByText("Maize meal 10kg")).toBeTruthy();
    expect(screen.getByText("12 / 20")).toBeTruthy();
    expect(screen.getByText("Alex Tuck")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Remove Alex Tuck" }));

    await waitFor(() => expect(screen.getByText("Alex Tuck was removed from the group.")).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/buying-groups/g1/members/s2", { callerPersonId: "p1" });
    expect(screen.queryByRole("button", { name: "Remove Thandi's Spaza" })).toBeNull();
  });
});
