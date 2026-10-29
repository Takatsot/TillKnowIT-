// @vitest-environment jsdom
import { fail, isSaleEntry, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { WalletPay } from "./WalletPay";

afterEach(cleanup);

const paid = { purchaseId: "pp1", holderId: "h1", holderName: "Sipho Mokoena", grade: "Grade 5", className: "5B", hasPhoto: true, amountCents: 1800, balanceCents: 4200, repeated: false };

async function fill(user: ReturnType<typeof userEvent.setup>, token = "QR-TOKEN-1") {
  await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
  await user.click(screen.getByRole("button", { name: "Add Bread" }));
  await user.type(screen.getByRole("textbox", { name: "Scan the QR code or type it" }), token);
}

describe("wallet-pay feature", () => {
  it("has no pack flag, because the pack has none for school wallets, and needs the server", () => {
    expect(feature.flag).toBeUndefined();
    expect(feature.requiresOnline).toBe(true);
    expect(feature.id.startsWith("core-")).toBe(false);
  });

  it("takes a payment by QR, shows who paid and queues the sale paid by wallet", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(paid));
    const { ctx, outbox, nudgeSync } = makeCtx({ api: { post } as never });
    render(<WalletPay ctx={ctx} />);

    await fill(user);
    await user.click(screen.getByRole("button", { name: /Take R\s?18,00 from the wallet/ }));

    expect(await screen.findByText(/Took R\s?18,00 from Sipho Mokoena's wallet/)).toBeTruthy();
    expect(screen.getByText("Sipho Mokoena, Grade 5 5B")).toBeTruthy();
    expect(screen.getByText(/Left in the wallet: R\s?42,00/)).toBeTruthy();
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/wallet-payments`);
    const body = post.mock.calls[0]![1] as { saleId: string; qrToken: string | null; token: string | null; amountCents: number; items: unknown[] };
    expect(body).toMatchObject({ deviceId: DEVICE, staffId: STAFF, qrToken: "QR-TOKEN-1", token: null, amountCents: 1800, useStaffAccount: false });
    expect(body.items).toEqual([{ productId: "p-bread", categoryCode: null, name: "Bread", quantity: 1, priceCents: 1800 }]);

    const sales = (await outbox.all()).filter(isSaleEntry);
    expect(sales).toHaveLength(1);
    expect(sales[0]!.sale.id).toBe(body.saleId);
    expect(sales[0]!.sale).toMatchObject({ paymentMethodCode: "split", totalCents: 1800, payments: [{ methodCode: "wallet", amountCents: 1800, reference: "pp1" }] });
    expect(nudgeSync).toHaveBeenCalled();
  });

  it("sends a card token as the card field", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(paid));
    const { ctx } = makeCtx({ api: { post } as never });
    render(<WalletPay ctx={ctx} />);

    await user.selectOptions(screen.getByRole("combobox", { name: "The code is from" }), "card");
    await fill(user, "04A1B2C3");
    await user.click(screen.getByRole("button", { name: /Take R\s?18,00 from the wallet/ }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ token: "04A1B2C3", qrToken: null });
  });

  it.each([
    ["wallet.insufficient", "There is not enough money in the wallet. The parent can top it up in the parent app."],
    ["qr.invalid", "This QR code has expired or is not valid. Ask the learner to open a fresh one on their phone."],
    ["wallet.daily_limit", "This would go over the daily spending limit."],
    ["token.blocked", "This card has been blocked. Ask the office."],
    ["wallet.item_blocked", "One of these items is blocked on this wallet by the parent."],
  ])("explains the decline %s in plain words and queues no sale", async (code, words) => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail(code, "A server message"));
    const { ctx, outbox } = makeCtx({ api: { post } as never });
    render(<WalletPay ctx={ctx} />);

    await fill(user);
    await user.click(screen.getByRole("button", { name: /Take R\s?18,00 from the wallet/ }));

    expect((await screen.findByRole("alert")).textContent).toContain(words);
    expect(await outbox.all()).toHaveLength(0);
  });

  it("shows the server's own message for a decline it has no words for, and reuses the sale id on a retry", async () => {
    const user = userEvent.setup();
    const post = vi.fn()
      .mockResolvedValueOnce(fail("network.unreachable", "We cannot reach the server. Check the connection and try again."))
      .mockResolvedValueOnce(ok(paid));
    const { ctx } = makeCtx({ api: { post } as never });
    render(<WalletPay ctx={ctx} />);

    await fill(user);
    await user.click(screen.getByRole("button", { name: /Take R\s?18,00 from the wallet/ }));
    expect((await screen.findByRole("alert")).textContent).toContain("We cannot reach the server.");
    await user.click(screen.getByRole("button", { name: /Take R\s?18,00 from the wallet/ }));

    await screen.findByText(/Took R\s?18,00/);
    expect((post.mock.calls[0]![1] as { saleId: string }).saleId).toBe((post.mock.calls[1]![1] as { saleId: string }).saleId);
  });

  it("will not charge without items or a code", async () => {
    const user = userEvent.setup();
    const post = vi.fn();
    const { ctx } = makeCtx({ api: { post } as never });
    render(<WalletPay ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: /from the wallet/ }));
    expect((await screen.findByRole("alert")).textContent).toBe("Add at least one item.");
    expect(post).not.toHaveBeenCalled();
  });
});
