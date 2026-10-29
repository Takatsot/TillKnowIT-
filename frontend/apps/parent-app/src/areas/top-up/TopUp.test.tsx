// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, sipho, thandi } from "../../shared/testing";
import { TopUp } from "./TopUp";

afterEach(cleanup);

const pending = { id: "t1", walletId: "w1", amountCents: 10000, status: "pending", checkoutUrl: "https://dev-partner.invalid/pay/dev_abc" };

describe("TopUp", () => {
  it("starts a top-up and shows it as waiting, with the pretend partner explained", async () => {
    const post = vi.fn(async () => ok(pending));
    mount(<TopUp />, { get: vi.fn(async () => ok([thandi])) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText(rand("R50,00"), { exact: false });

    await user.type(screen.getByLabelText("Amount in rand"), "100");
    await user.click(screen.getByRole("button", { name: "Add money" }));

    expect(await screen.findByText("Waiting for payment")).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/wallets/w1/top-ups", expect.objectContaining({ callerPersonId: "p1", amountCents: 10000 }));
    expect(screen.getByRole("link", { name: "Go to the payment page" }).getAttribute("href")).toBe(pending.checkoutUrl);
    expect(screen.getByText(/pretend one/)).toBeTruthy();
  });

  it("shows the money as arrived once the balance has gone up", async () => {
    const get = vi.fn()
      .mockResolvedValueOnce(ok([thandi]))
      .mockResolvedValue(ok([{ ...thandi, balanceCents: 15000 }]));
    mount(<TopUp />, { get: get as never, post: vi.fn(async () => ok(pending)) as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText(rand("R50,00"), { exact: false });
    await user.type(screen.getByLabelText("Amount in rand"), "100");
    await user.click(screen.getByRole("button", { name: "Add money" }));
    await screen.findByText("Waiting for payment");

    await user.click(screen.getByRole("button", { name: "Check my balance" }));

    expect(await screen.findByText("Money arrived")).toBeTruthy();
  });

  it("shows the server's refusal, such as no partner connected", async () => {
    const post = vi.fn(async () => fail("payment.partner_not_connected", "No payment partner is connected yet."));
    mount(<TopUp />, { get: vi.fn(async () => ok([thandi])) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText(rand("R50,00"), { exact: false });

    await user.type(screen.getByLabelText("Amount in rand"), "100");
    await user.click(screen.getByRole("button", { name: "Add money" }));

    expect((await screen.findByRole("alert")).textContent).toBe("No payment partner is connected yet.");
  });

  it("says when there is no one to move money to", async () => {
    mount(<TopUp />, { get: vi.fn(async () => ok([thandi])) as never });

    expect(await screen.findByText("You need two children linked to move money between them.")).toBeTruthy();
  });

  it("moves money between two children and reloads the balances", async () => {
    const get = vi.fn(async () => ok([thandi, sipho]));
    const post = vi.fn(async () => ok(undefined));
    mount(<TopUp />, { get: get as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Move money between my children");

    await user.type(screen.getByLabelText("Amount to move in rand"), "20");
    await user.click(screen.getByRole("button", { name: "Move money" }));

    await waitFor(() => expect(screen.getByText(rand("R20,00 was moved from Thandi Dlamini to Sipho Dlamini."), { exact: false })).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/wallets/transfers", expect.objectContaining({ callerPersonId: "p1", fromWalletId: "w1", toWalletId: "w2", amountCents: 2000, reference: expect.any(String) }));
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("shows the server's refusal when there is not enough money", async () => {
    const post = vi.fn(async () => fail("wallet.insufficient", "Not enough money in that wallet."));
    mount(<TopUp />, { get: vi.fn(async () => ok([thandi, sipho])) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Move money between my children");

    await user.type(screen.getByLabelText("Amount to move in rand"), "900");
    await user.click(screen.getByRole("button", { name: "Move money" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Not enough money in that wallet.");
  });
});
