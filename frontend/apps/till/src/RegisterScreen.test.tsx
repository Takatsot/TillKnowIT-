// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RegisterScreen } from "./RegisterScreen";
import { loadDeviceConfig } from "./syncRunner";

beforeEach(() => localStorage.clear());
afterEach(cleanup);

const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";

describe("RegisterScreen", () => {
  it("registers the till with the two ids from the back office and remembers them", async () => {
    const onRegistered = vi.fn();
    const user = userEvent.setup();
    render(<RegisterScreen language="en" onRegistered={onRegistered} />);

    await user.type(screen.getByRole("textbox", { name: "Shop number" }), SHOP);
    await user.type(screen.getByRole("textbox", { name: "Till number" }), DEVICE);
    await user.click(screen.getByRole("button", { name: "Register till" }));

    expect(onRegistered).toHaveBeenCalledWith({ shopId: SHOP, deviceId: DEVICE });
    expect(loadDeviceConfig()).toEqual({ shopId: SHOP, deviceId: DEVICE });
  });

  it("says which number is wrong and registers nothing", async () => {
    const onRegistered = vi.fn();
    const user = userEvent.setup();
    render(<RegisterScreen language="en" onRegistered={onRegistered} />);

    await user.type(screen.getByRole("textbox", { name: "Shop number" }), "12345");
    await user.type(screen.getByRole("textbox", { name: "Till number" }), DEVICE);
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect(screen.getByRole("alert").textContent).toContain("shop number does not look right");

    await user.clear(screen.getByRole("textbox", { name: "Shop number" }));
    await user.type(screen.getByRole("textbox", { name: "Shop number" }), SHOP);
    await user.clear(screen.getByRole("textbox", { name: "Till number" }));
    await user.type(screen.getByRole("textbox", { name: "Till number" }), "oops");
    await user.click(screen.getByRole("button", { name: "Register till" }));
    expect(screen.getByRole("alert").textContent).toContain("till number does not look right");

    expect(onRegistered).not.toHaveBeenCalled();
    expect(loadDeviceConfig()).toBeNull();
  });

  it("keeps the button off until both are typed, and offers the example data only when asked to", async () => {
    const onSkip = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(<RegisterScreen language="en" onRegistered={vi.fn()} />);
    expect((screen.getByRole("button", { name: "Register till" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByRole("button", { name: /example data/ })).toBeNull();

    rerender(<RegisterScreen language="en" onRegistered={vi.fn()} onSkip={onSkip} />);
    await user.click(screen.getByRole("button", { name: "Use example data (development only)" }));
    expect(onSkip).toHaveBeenCalled();
  });

  it("says so when the browser will not store the registration", async () => {
    const original = Storage.prototype.setItem;
    Storage.prototype.setItem = () => { throw new Error("full"); };
    try {
      const user = userEvent.setup();
      render(<RegisterScreen language="en" onRegistered={vi.fn()} />);
      await user.type(screen.getByRole("textbox", { name: "Shop number" }), SHOP);
      await user.type(screen.getByRole("textbox", { name: "Till number" }), DEVICE);
      await user.click(screen.getByRole("button", { name: "Register till" }));

      expect(screen.getByRole("alert").textContent).toContain("could not save its registration");
    } finally {
      Storage.prototype.setItem = original;
    }
  });
});
