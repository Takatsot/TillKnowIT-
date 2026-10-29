// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { Vouchers } from "./Vouchers";
import { redeemVoucher } from "./voucherApi";

afterEach(cleanup);

describe("vouchers feature", () => {
  it("is switched on by the vouchers flag", () => {
    expect(feature.flag).toBe("vouchers");
    expect(feature.requiresOnline).toBe(true);
  });

  it("sells a voucher and shows the code once", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok({ id: "v1", code: "ABCD-EFGH-1234", balanceCents: 20000, expiresOn: "2027-10-26" }));
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: "Voucher value" }), "200");
    await user.click(screen.getByRole("button", { name: "Sell voucher" }));

    expect(await screen.findByText("ABCD-EFGH-1234")).toBeTruthy();
    expect(screen.getByText(/shown only once/)).toBeTruthy();
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/vouchers`);
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF, amountCents: 20000, paidWith: "cash" });
    expect(String((post.mock.calls[0]![1] as { reference: string }).reference).length).toBeGreaterThan(8);

    await user.click(screen.getByRole("button", { name: "I have given the code to the customer" }));
    expect(screen.queryByText("ABCD-EFGH-1234")).toBeNull();
  });

  it("sends the same reference again after a failure so a voucher is never sold twice", async () => {
    const user = userEvent.setup();
    const post = vi.fn()
      .mockResolvedValueOnce(fail("network.unreachable", "We cannot reach the server."))
      .mockResolvedValueOnce(ok({ id: "v1", code: "CODE-1", balanceCents: 5000, expiresOn: null }));
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: "Voucher value" }), "50");
    await user.click(screen.getByRole("button", { name: "Sell voucher" }));
    expect((await screen.findByRole("alert")).textContent).toContain("cannot reach");
    await user.click(screen.getByRole("button", { name: "Sell voucher" }));

    expect(await screen.findByText("CODE-1")).toBeTruthy();
    expect((post.mock.calls[0]![1] as { reference: string }).reference).toBe((post.mock.calls[1]![1] as { reference: string }).reference);
  });

  it("rejects a value that is not money", async () => {
    const user = userEvent.setup();
    const post = vi.fn();
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: "Voucher value" }), "abc");
    await user.click(screen.getByRole("button", { name: "Sell voucher" }));

    expect((await screen.findByRole("alert")).textContent).toContain("Type the value in rand");
    expect(post).not.toHaveBeenCalled();
  });

  it("checks a balance and shows an expired voucher plainly", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => ok({ id: "v1", codeLast4: "1234", balanceCents: 7500, expiresOn: "2026-01-01", isVoid: false, isExpired: true }));
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Check a balance" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD-1234");
    await user.click(screen.getByRole("button", { name: "Check balance" }));

    expect(await screen.findByText(/Voucher ending 1234 has R\s?75,00 left/)).toBeTruthy();
    expect(screen.getByText("This voucher has expired.")).toBeTruthy();
    expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/vouchers/check`, { code: "ABCD-1234", deviceId: DEVICE, staffId: STAFF });
  });

  it("redeems and gives the payment reference a sale can use", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok({ id: "v1", codeLast4: "1234", balanceCents: 2500, expiresOn: null, isVoid: false, isExpired: false }));
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Use a voucher" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD-1234");
    await user.type(screen.getByRole("textbox", { name: "Amount to take off the voucher" }), "50");
    await user.click(screen.getByRole("button", { name: "Use voucher" }));

    expect(await screen.findByText(/Took R\s?50,00 off the voucher. R\s?25,00 left/)).toBeTruthy();
    const sent = post.mock.calls[0]![1] as { reference: string; amountCents: number };
    expect(sent.amountCents).toBe(5000);
    expect(screen.getByText(`Payment reference for the sale: ${sent.reference}`)).toBeTruthy();
  });

  it("shows a refusal when the voucher has too little left", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail("voucher.insufficient", "Only 1000 cents is left on this voucher."));
    const { ctx } = makeCtx({ flags: ["vouchers"], api: { post } as never });
    render(<Vouchers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Use a voucher" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "X");
    await user.type(screen.getByRole("textbox", { name: "Amount to take off the voucher" }), "50");
    await user.click(screen.getByRole("button", { name: "Use voucher" }));

    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("Only 1000 cents"));
  });

  it("redeemVoucher returns the reference and the balance left for the sell screen to use", async () => {
    const api = { post: vi.fn(async () => ok({ id: "v", codeLast4: "1", balanceCents: 100, expiresOn: null, isVoid: false, isExpired: false })) };
    const r = await redeemVoucher(api as never, { shopId: SHOP, deviceId: DEVICE, staffId: STAFF }, { code: " C ", amountCents: 900, reference: "ref-1", saleId: "sale-1" });

    expect(r).toEqual({ ok: true, value: { reference: "ref-1", amountCents: 900, balanceCents: 100 } });
    expect(api.post).toHaveBeenCalledWith(`/shops/${SHOP}/vouchers/redeem`, { deviceId: DEVICE, staffId: STAFF, code: "C", amountCents: 900, reference: "ref-1", saleId: "sale-1" });
  });
});
