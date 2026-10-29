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

/** A fake server that keeps staff and tills, so a refresh after a change shows the change. */
function server(initial: { staff?: unknown[]; devices?: unknown[] } = {}) {
  const state = { staff: [...(initial.staff ?? [])] as Record<string, unknown>[], devices: [...(initial.devices ?? [])] as Record<string, unknown>[] };
  const get = vi.fn(async (path: string) => (path === "/shops/s1/staff" ? ok(state.staff) : path === "/shops/s1/devices" ? ok(state.devices) : ok([])));
  return { state, get };
}

function mount(api: Partial<Api>) {
  const fallback = server();
  render(<SessionProvider api={{ get: fallback.get, ...api } as Api} initial={session}><StaffDevices /></SessionProvider>);
}

async function addStaff(user: ReturnType<typeof userEvent.setup>, pin: string) {
  await user.type(screen.getByLabelText("Name"), "Lindiwe");
  await user.type(screen.getByLabelText("Phone number"), "0821234567");
  await user.type(screen.getByLabelText("PIN (4 to 6 digits)"), pin);
  await user.click(screen.getByRole("button", { name: "Add staff member" }));
}

describe("Staff and tills", () => {
  it("lists the staff and tills the server has, with their ids", async () => {
    const { get } = server({
      staff: [{ staffId: "m7", name: "Thandi", phone: "0821111111", role: "manager", disabled: false }],
      devices: [{ deviceId: "d7", name: "Counter 2", status: "approved", lastSeenAt: null, wipeRequested: false }],
    });
    mount({ get: get as never });

    expect(await screen.findByText("Thandi")).toBeTruthy();
    expect(screen.getByText("m7")).toBeTruthy();
    expect(await screen.findByText("Counter 2")).toBeTruthy();
    expect(screen.getByText("Approved")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/shops/s1/staff", { callerPersonId: "p1" });
    expect(get).toHaveBeenCalledWith("/shops/s1/devices", { callerPersonId: "p1" });
  });

  it("shows the empty states", async () => {
    mount({});
    expect(await screen.findByText("No staff yet.")).toBeTruthy();
    expect(screen.getByText("No tills registered yet.")).toBeTruthy();
  });

  it("adds staff, refreshes the list and can change their PIN and disable them", async () => {
    const s = server();
    const post = vi.fn(async () => {
      s.state.staff.push({ staffId: "m1", name: "Lindiwe", phone: "0821234567", role: "cashier", disabled: false });
      return ok({ membershipId: "m1" });
    });
    const patch = vi.fn(async (_p: string, body: { disabled?: boolean }) => {
      if (body.disabled !== undefined) s.state.staff[0]!.disabled = body.disabled;
      return ok(undefined);
    });
    mount({ get: s.get as never, post: post as never, patch: patch as never });
    const user = userEvent.setup();

    await addStaff(user, "1234");
    expect(await screen.findByText(/Lindiwe was added/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/shops/s1/staff", { name: "Lindiwe", phone: "0821234567", roleCode: "cashier", pin: "1234" });

    await user.type(await screen.findByLabelText("New PIN for Lindiwe"), "654321");
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

  it("registers a till, shows its tillknowit.device value, approves it and wipes it after confirming", async () => {
    const s = server();
    const post = vi.fn(async (path: string) => {
      if (path.endsWith("/devices")) { s.state.devices.push({ deviceId: "d1", name: "Counter 1", status: "pending", lastSeenAt: null, wipeRequested: false }); return ok({ deviceId: "d1", enrolmentCode: "WXYZ-4321" }); }
      if (path === "/devices/d1/approve") s.state.devices[0]!.status = "approved";
      if (path === "/devices/d1/wipe") s.state.devices[0]!.wipeRequested = true;
      return ok(undefined);
    });
    mount({ get: s.get as never, post: post as never });
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Till name"), "Counter 1");
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect(await screen.findByText("Set up the till Counter 1")).toBeTruthy();
    expect(screen.getByText('{"shopId":"s1","deviceId":"d1"}')).toBeTruthy();
    expect(screen.getByText("WXYZ-4321")).toBeTruthy();
    expect(screen.getByText(/Write this setup code down now. It is not shown again./)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/shops/s1/devices", { callerPersonId: "p1", name: "Counter 1" });
    expect(await screen.findByText("Waiting for approval")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Approve Counter 1" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/devices/d1/approve", undefined, { callerPersonId: "p1" }));
    expect(await screen.findByText("Approved")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Wipe Counter 1" }));
    expect(post).not.toHaveBeenCalledWith("/devices/d1/wipe", undefined, { callerPersonId: "p1" });
    await user.click(screen.getByRole("button", { name: "Yes, wipe Counter 1" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/devices/d1/wipe", undefined, { callerPersonId: "p1" }));
    expect(await screen.findByText("Wipe requested")).toBeTruthy();
  });

  it("resets a till after confirming and shows the new setup code once", async () => {
    const { get } = server({ devices: [{ deviceId: "d7", name: "Counter 2", status: "approved", lastSeenAt: null, wipeRequested: false }] });
    const post = vi.fn(async () => ok({ deviceId: "d7", enrolmentCode: "NEWCODE789" }));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Reset Counter 2" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, reset Counter 2" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/devices/d7/reset-credential", undefined, { callerPersonId: "p1" }));
    expect(await screen.findByText("NEWCODE789")).toBeTruthy();
    expect(screen.getByText("Counter 2 was reset. Type the new setup code into the till, then approve it.")).toBeTruthy();
  });

  it("shows the setup value for a till chosen from the list", async () => {
    const { get } = server({ devices: [{ deviceId: "d7", name: "Counter 2", status: "approved", lastSeenAt: null, wipeRequested: false }] });
    mount({ get: get as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Show setup for Counter 2" }));
    expect(screen.getByText('{"shopId":"s1","deviceId":"d7"}')).toBeTruthy();
    expect(screen.getByText(/cannot be shown again/)).toBeTruthy();
    expect(screen.getByText(/can be reset: its old credential stops working/)).toBeTruthy();
  });

  it("shows the server's message when registering fails", async () => {
    mount({ post: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) as never });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Till name"), "Counter 1");
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
  });
});
