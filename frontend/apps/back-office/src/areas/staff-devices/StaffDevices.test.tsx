// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StaffDevices } from "./StaffDevices";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><StaffDevices /></SessionProvider>);
}

async function addStaff(user: ReturnType<typeof userEvent.setup>, pin: string) {
  await user.type(screen.getByLabelText("Name"), "Lindiwe");
  await user.type(screen.getByLabelText("Phone number"), "0821234567");
  await user.type(screen.getByLabelText("PIN (4 to 6 digits)"), pin);
  await user.click(screen.getByRole("button", { name: "Add staff member" }));
}

describe("Staff and tills", () => {
  it("shows the empty states and says the server has no lists", () => {
    mount({});
    expect(screen.getByText("Nobody added in this visit yet.")).toBeTruthy();
    expect(screen.getByText("No tills registered in this visit yet.")).toBeTruthy();
    expect(screen.getByText(/server has no list of tills/)).toBeTruthy();
  });

  it("adds staff, keeps them in the list and can change their PIN and disable them", async () => {
    const post = vi.fn(async () => ok({ membershipId: "m1" }));
    const patch = vi.fn(async () => ok(undefined));
    mount({ post: post as never, patch: patch as never });
    const user = userEvent.setup();

    await addStaff(user, "1234");
    expect(await screen.findByText(/Lindiwe was added/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/shops/s1/staff", { name: "Lindiwe", phone: "0821234567", roleCode: "cashier", pin: "1234" });

    await user.type(screen.getByLabelText("New PIN for Lindiwe"), "654321");
    await user.click(screen.getByRole("button", { name: "Change PIN for Lindiwe" }));
    await waitFor(() => expect(patch).toHaveBeenCalledWith("/staff/m1", { pin: "654321" }));

    await user.click(screen.getByRole("button", { name: "Disable Lindiwe" }));
    await waitFor(() => expect(patch).toHaveBeenCalledWith("/staff/m1", { disabled: true }));
    expect(await screen.findByRole("button", { name: "Enable Lindiwe" })).toBeTruthy();
  });

  it("refuses a short PIN without calling the server and shows the server's refusal otherwise", async () => {
    const post = vi.fn(async () => fail("staff.already_member", "That person already works here."));
    mount({ post: post as never });
    const user = userEvent.setup();

    await addStaff(user, "12");
    expect((await screen.findByRole("alert")).textContent).toBe("A PIN is 4 to 6 digits.");
    expect(post).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText("PIN (4 to 6 digits)"));
    await user.type(screen.getByLabelText("PIN (4 to 6 digits)"), "1234");
    await user.click(screen.getByRole("button", { name: "Add staff member" }));
    expect(await screen.findByText("That person already works here.")).toBeTruthy();
  });

  it("registers a till, shows the ids it needs, approves it and wipes it after confirming", async () => {
    const post = vi.fn(async (path: string) => (path.endsWith("/devices") ? ok({ deviceId: "d1" }) : ok(undefined)));
    mount({ post: post as never });
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Till name"), "Counter 1");
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect(await screen.findByText("Set up the till Counter 1")).toBeTruthy();
    expect(screen.getByText('{"shopId":"s1","deviceId":"d1"}')).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/shops/s1/devices", { name: "Counter 1" });

    await user.click(screen.getByRole("button", { name: "Approve Counter 1" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/devices/d1/approve"));
    expect(await screen.findByText("Approved")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Wipe Counter 1" }));
    expect(post).not.toHaveBeenCalledWith("/devices/d1/wipe");
    await user.click(screen.getByRole("button", { name: "Yes, wipe Counter 1" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/devices/d1/wipe"));
    expect(await screen.findByText("Wipe requested")).toBeTruthy();
  });

  it("shows the server's message when registering fails", async () => {
    mount({ post: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) as never });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Till name"), "Counter 1");
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
  });
});
