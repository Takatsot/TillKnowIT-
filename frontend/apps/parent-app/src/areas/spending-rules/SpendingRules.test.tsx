// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, thandi } from "../../shared/testing";
import { SpendingRules } from "./SpendingRules";

afterEach(cleanup);

const wallet = {
  id: "w1", holderId: "h1", holderName: "Thandi Dlamini", balanceCents: 5000, dailyLimitCents: 2000, spentTodayCents: 500, status: "active",
  restrictions: [{ kind: "category", key: "sweets" }], notifyOnPurchase: false, lowBalanceAlertCents: null,
};

function api(extra: Record<string, unknown> = {}) {
  const get = vi.fn(async (path: string) => {
    if (path === "/me/children") return ok([thandi]);
    if (path === "/wallets/w1") return ok(wallet);
    return ok({ breaks: [], items: [{ id: "m1", name: "Chips", priceCents: 800, productId: "chips", categoryCode: "snacks", isAvailable: true }] });
  });
  return { get: get as never, ...extra };
}

describe("SpendingRules", () => {
  it("shows the limit, what was spent and each blocked rule in plain words", async () => {
    mount(<SpendingRules />, api());

    expect(await screen.findByText("Nothing in the group \"sweets\" can be bought.")).toBeTruthy();
    expect((screen.getByLabelText("Daily limit in rand") as HTMLInputElement).value).toBe("20");
    expect(screen.getByText(rand("Spent today: R5,00"), { exact: false })).toBeTruthy();
  });

  it("saves the daily limit and alerts", async () => {
    const put = vi.fn(async () => ok(wallet));
    mount(<SpendingRules />, api({ put }));
    const user = userEvent.setup({ delay: null });
    const field = await screen.findByLabelText("Daily limit in rand");

    await user.clear(field);
    await user.type(field, "15,50");
    await user.click(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Your rules were saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/wallets/w1/limits", { callerPersonId: "p1", dailyLimitCents: 1550, notifyOnPurchase: true, lowBalanceAlertCents: null, clearLowBalanceAlert: true });
  });

  it("shows the server's refusal for a limit that is too high", async () => {
    const put = vi.fn(async () => fail("wallet.limit_invalid", "The daily limit is from R0 to R5000."));
    mount(<SpendingRules />, api({ put }));
    const user = userEvent.setup({ delay: null });
    await screen.findByLabelText("Daily limit in rand");

    await user.click(screen.getByRole("button", { name: "Save" }));

    expect((await screen.findByRole("alert")).textContent).toBe("The daily limit is from R0 to R5000.");
  });

  it("blocks a new item by sending the whole list and allows one again", async () => {
    const put = vi.fn(async () => ok(wallet));
    mount(<SpendingRules />, api({ put }));
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Nothing in the group \"sweets\" can be bought.");

    await user.click(await screen.findByRole("button", { name: "Chips" }));
    await user.click(screen.getByRole("button", { name: "Block it" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith("/wallets/w1/restrictions", { callerPersonId: "p1", restrictions: [{ kind: "category", key: "sweets" }, { kind: "product", key: "chips" }] }));

    await user.click(screen.getByRole("button", { name: "Allow sweets again" }));
    await waitFor(() => expect(put).toHaveBeenLastCalledWith("/wallets/w1/restrictions", { callerPersonId: "p1", restrictions: [] }));
  });

  it("says nothing is blocked when the list is empty", async () => {
    const get = vi.fn(async (path: string) => (path === "/me/children" ? ok([thandi]) : path === "/wallets/w1" ? ok({ ...wallet, restrictions: [] }) : fail("permission.denied", "no")));
    mount(<SpendingRules />, { get: get as never });

    expect(await screen.findByText("Nothing is blocked.")).toBeTruthy();
  });
});
