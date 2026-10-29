// @vitest-environment jsdom
import { hashPin } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { PinUnlock } from "./PinUnlock";
import type { StaffRecord } from "./staffStore";

let thandi: StaffRecord;
beforeAll(async () => {
  thandi = { id: "t1", name: "Thandi", role: "cashier", pinHash: await hashPin("1234") };
});

beforeEach(() => localStorage.clear());
afterEach(cleanup);

const button = (name: string) => screen.getByRole("button", { name }) as HTMLButtonElement;

async function enter(user: ReturnType<typeof userEvent.setup>, pin: string) {
  for (const d of pin) await user.click(screen.getByRole("button", { name: d }));
  await user.click(screen.getByRole("button", { name: "OK" }));
}

/** Each try is checked (slowly, by design) before the next is typed, so wait for its answer instead of racing it. */
const SLOW = { timeout: 15_000 };
async function wrongTries(user: ReturnType<typeof userEvent.setup>, count: number) {
  for (let left = 4; left > 4 - count && left >= 0; left--) {
    await enter(user, "0000");
    await screen.findByText(left === 0 ? /Too many wrong tries/ : `That PIN is not right. Tries left: ${left}.`, undefined, SLOW);
  }
}

describe("PinUnlock", () => {
  it("unlocks with the right PIN", async () => {
    const onUnlock = vi.fn();
    const user = userEvent.setup();
    render(<PinUnlock staff={[thandi]} language="en" onUnlock={onUnlock} />);

    await user.click(screen.getByRole("button", { name: "Thandi" }));
    await enter(user, "1234");

    await waitFor(() => expect(onUnlock).toHaveBeenCalledWith(thandi));
  });

  it("OK stays disabled until the PIN is 4 digits", async () => {
    const user = userEvent.setup();
    render(<PinUnlock staff={[thandi]} language="en" onUnlock={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Thandi" }));

    await user.click(screen.getByRole("button", { name: "1" }));
    await user.click(screen.getByRole("button", { name: "2" }));
    await user.click(screen.getByRole("button", { name: "3" }));
    expect(button("OK").disabled).toBe(true);

    await user.click(screen.getByRole("button", { name: "4" }));
    expect(button("OK").disabled).toBe(false);
  });

  it("counts down wrong tries, then locks the pad after five", { timeout: 30_000 }, async () => {
    const onUnlock = vi.fn();
    const user = userEvent.setup();
    render(<PinUnlock staff={[thandi]} language="en" onUnlock={onUnlock} />);
    await user.click(screen.getByRole("button", { name: "Thandi" }));

    await enter(user, "0000");
    expect(await screen.findByText("That PIN is not right. Tries left: 4.")).toBeTruthy();

    // Each try is checked (slowly, by design) before the next is typed, so wait for its answer instead of racing it.
    for (const left of [3, 2, 1]) {
      await enter(user, "0000");
      expect(await screen.findByText(`That PIN is not right. Tries left: ${left}.`, undefined, { timeout: 15_000 })).toBeTruthy();
    }

    await enter(user, "0000");
    expect(await screen.findByText("Too many wrong tries. Try again in 5 min.", undefined, { timeout: 15_000 })).toBeTruthy();
    expect(button("1").disabled).toBe(true);
    expect(button("OK").disabled).toBe(true);
    expect(onUnlock).not.toHaveBeenCalled();
  });

  it("stays locked after a restart, so closing the app is no way round the lock", { timeout: 60_000 }, async () => {
    const user = userEvent.setup();
    const first = render(<PinUnlock staff={[thandi]} language="en" onUnlock={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Thandi" }));
    await wrongTries(user, 5);
    first.unmount();

    render(<PinUnlock staff={[thandi]} language="en" onUnlock={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Thandi" }));

    expect(await screen.findByText(/Too many wrong tries/, undefined, SLOW)).toBeTruthy();
    expect(button("1").disabled).toBe(true);
  });

  it("tells people when no staff exist rather than showing an empty screen", () => {
    render(<PinUnlock staff={[]} language="en" onUnlock={vi.fn()} />);
    expect(screen.getByText(/No staff on this till yet/)).toBeTruthy();
  });
});
