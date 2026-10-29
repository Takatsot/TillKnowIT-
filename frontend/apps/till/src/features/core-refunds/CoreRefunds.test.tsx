// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { hashPin, newId, queueSale, type OutboxEntry, type RecordOutboxEntry } from "@tillknowit/domain";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { aSale, makeCoreCtx, STAFF } from "../../coreTesting";
import { cashUpFigures } from "../../ledger";
import { saveStaff } from "../../staffStore";
import { CoreRefunds, approvalThreshold } from "./CoreRefunds";

beforeEach(() => localStorage.clear());
afterEach(cleanup);

const records = (all: OutboxEntry[], kind: string) => (all as RecordOutboxEntry[]).filter((e) => e.kind === kind).map((e) => e.record as Record<string, unknown>);
const sell = (outbox: Parameters<typeof queueSale>[0], over: Parameters<typeof aSale>[0] = {}) => queueSale(outbox, aSale(over), new Date());

describe("Past sales tab", () => {
  it("says so when no sale has been made on this till", async () => {
    const { ctx } = makeCoreCtx({ flags: ["refunds"] });
    render(<CoreRefunds ctx={ctx} />);

    expect(await screen.findByText("No sales on this till yet.")).toBeTruthy();
  });

  it("lists the till's own recent sales, newest first, and finds one by item name", async () => {
    const { ctx } = makeCoreCtx({ flags: ["refunds"] });
    await sell(ctx.outbox, { id: newId(1), soldAt: "2026-10-26T08:00:00.000Z" });
    await sell(ctx.outbox, { id: newId(2), soldAt: "2026-10-26T09:00:00.000Z", lines: [{ productId: "p-milk", name: "Milk 1L", quantity: 1, priceCents: 2199, costCents: 1700, unitDivisor: 1, discountCents: 0, promotionId: null }], totalCents: 2199, tenderedCents: 2199, changeCents: 0 });
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);

    const rows = await screen.findAllByRole("row");
    expect(rows[1]!.textContent).toContain("Milk 1L");
    await user.type(screen.getByRole("textbox", { name: "Find by item name or amount" }), "bread");
    expect(screen.queryByText("Milk 1L")).toBeNull();
    await user.clear(screen.getByRole("textbox", { name: "Find by item name or amount" }));
    await user.type(screen.getByRole("textbox", { name: "Find by item name or amount" }), "zzz");
    expect(screen.getByText("No sale matches that.")).toBeTruthy();
  });

  it("refunds part of a cash sale with a reason, queues it, and the till's books show the cash going out", async () => {
    const { ctx, outbox, kv, nudgeSync } = makeCoreCtx({ flags: ["refunds"] });
    const sale = aSale();
    await queueSale(outbox, sale, new Date());
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    await user.type(screen.getByRole("textbox", { name: "Quantity to take back of Bread (up to 2)" }), "1");
    await user.type(screen.getByRole("textbox", { name: "Reason for the refund" }), "Stale");
    expect(screen.getByText(/Refund: .*18/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Refund" }));

    await waitFor(() => expect(screen.getByText(/Refund of .* saved\./)).toBeTruthy());
    const [refund] = records(await outbox.all(), "refund");
    expect(refund).toMatchObject({ saleId: sale.id, reason: "Stale", methodCode: null, approvedBy: null, lines: [{ productId: "p-bread", quantity: 1 }], staffId: STAFF });
    expect(nudgeSync).toHaveBeenCalled();
    const figures = await cashUpFigures(kv, new Date().toISOString().slice(0, 10), 0, new Date(Date.now() + 1000));
    expect(figures.cashRefundsCents).toBe(1800);

    // What is left to take back has gone down.
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));
    expect(screen.getByRole("textbox", { name: "Quantity to take back of Bread (up to 1)" })).toBeTruthy();
  });

  it("will not take back more than was sold, or nothing, or without a reason", async () => {
    const { ctx, outbox } = makeCoreCtx({ flags: ["refunds"] });
    await sell(ctx.outbox);
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    await user.click(screen.getByRole("button", { name: "Refund" }));
    expect(screen.getByRole("alert").textContent).toBe("Choose how many of what is coming back.");
    await user.type(screen.getByRole("textbox", { name: /Quantity to take back/ }), "3");
    await user.click(screen.getByRole("button", { name: "Refund" }));
    expect(screen.getByRole("alert").textContent).toBe("You cannot take back more than was sold.");
    await user.clear(screen.getByRole("textbox", { name: /Quantity to take back/ }));
    await user.type(screen.getByRole("textbox", { name: /Quantity to take back/ }), "1");
    await user.click(screen.getByRole("button", { name: "Refund" }));
    expect(screen.getByRole("alert").textContent).toBe("Say why the goods are coming back.");
    expect(records(await outbox.all(), "refund")).toEqual([]);
  });

  it("needs a manager's PIN above the threshold when a cashier refunds, and records who approved", async () => {
    const { ctx, outbox, kv } = makeCoreCtx({ flags: [{ flag: "refunds", settingsJson: JSON.stringify({ approvalThresholdCents: 1000 }) }], role: "cashier" });
    await saveStaff(kv, [{ id: "boss-1", name: "Sipho", role: "manager", pinHash: await hashPin("4321") }, { id: STAFF, name: "Thandi", role: "cashier", pinHash: "x" }]);
    await sell(ctx.outbox);
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));
    await user.type(screen.getByRole("textbox", { name: /Quantity to take back/ }), "2");
    await user.type(screen.getByRole("textbox", { name: "Reason for the refund" }), "Faulty");

    await user.click(screen.getByRole("button", { name: "Ask a manager" }));
    await user.type(screen.getByLabelText("Manager PIN"), "1111");
    await user.click(screen.getByRole("button", { name: "Approve" }));
    expect(await screen.findByText("That PIN is not right. Tries left: 4.")).toBeTruthy();
    expect(records(await outbox.all(), "refund")).toEqual([]);

    await user.type(screen.getByLabelText("Manager PIN"), "4321");
    await user.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(screen.getByText(/Refund of .* saved\./)).toBeTruthy());
    expect(records(await outbox.all(), "refund")[0]).toMatchObject({ approvedBy: "boss-1", lines: [{ productId: "p-bread", quantity: 2 }] });
  });

  it("lets a manager approve their own refund above the threshold", async () => {
    const { ctx, outbox } = makeCoreCtx({ flags: [{ flag: "refunds", settingsJson: JSON.stringify({ approvalThresholdCents: 1000 }) }], role: "manager" });
    await sell(ctx.outbox);
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));
    await user.type(screen.getByRole("textbox", { name: /Quantity to take back/ }), "2");
    await user.type(screen.getByRole("textbox", { name: "Reason for the refund" }), "Faulty");

    await user.click(screen.getByRole("button", { name: "Refund" }));

    await waitFor(() => expect(screen.getByText(/Refund of .* saved\./)).toBeTruthy());
    expect(records(await outbox.all(), "refund")[0]).toMatchObject({ approvedBy: STAFF });
  });

  it("says there is no manager to ask when none was downloaded", async () => {
    const { ctx, kv } = makeCoreCtx({ flags: [{ flag: "refunds", settingsJson: JSON.stringify({ approvalThresholdCents: 100 }) }], role: "cashier" });
    await saveStaff(kv, [{ id: STAFF, name: "Thandi", role: "cashier", pinHash: "x" }]);
    await sell(ctx.outbox);
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));
    await user.type(screen.getByRole("textbox", { name: /Quantity to take back/ }), "1");
    await user.type(screen.getByRole("textbox", { name: "Reason for the refund" }), "Faulty");

    await user.click(screen.getByRole("button", { name: "Ask a manager" }));

    expect((await screen.findByRole("alert")).textContent).toContain("no manager on this till");
  });

  it("sends a split, wallet, voucher or lay-by sale online instead of refunding it here", async () => {
    const { ctx } = makeCoreCtx({ flags: ["refunds"] });
    await sell(ctx.outbox, { paymentMethodCode: "split", tenderedCents: null, changeCents: null, payments: [{ methodCode: "card", amountCents: 1800, reference: null, tenderedCents: null }, { methodCode: "wallet", amountCents: 1800, reference: "w1", tenderedCents: null }] });
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    expect(screen.getByText(/has to be refunded online/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Refund" })).toBeNull();
  });

  it("says refunds are off when the pack does not switch them on", async () => {
    const { ctx } = makeCoreCtx({ flags: [] });
    await sell(ctx.outbox);
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    expect(screen.getByText("Refunds are not switched on for this shop.")).toBeTruthy();
  });

  it("lets a manager cancel a whole sale with a reason, and takes a credit sale off the customer's account", async () => {
    const { ctx, outbox, kv } = makeCoreCtx({ flags: ["refunds"], role: "manager" });
    const sale = aSale({ paymentMethodCode: "credit", accountId: "acc-1", tenderedCents: null, changeCents: null });
    await queueSale(outbox, sale, new Date());
    const user = userEvent.setup();
    render(<CoreRefunds ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    await user.click(screen.getByRole("button", { name: "Cancel this sale" }));
    expect((screen.getByRole("button", { name: "Confirm cancellation" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByRole("textbox", { name: "Reason for cancelling" }), "Customer left");
    await user.click(screen.getByRole("button", { name: "Confirm cancellation" }));

    await waitFor(() => expect(screen.getByText("The sale was cancelled.")).toBeTruthy());
    const all = await outbox.all();
    expect(records(all, "cancellation")).toEqual([expect.objectContaining({ saleId: sale.id, reason: "Customer left" })]);
    expect(records(all, "credit_entry")).toEqual([expect.objectContaining({ type: "cancel", amountCents: -3600, accountId: "acc-1", saleId: sale.id })]);
    // The till's books know the sale is cancelled.
    expect(((await kv.get<Array<{ cancelled: boolean }>>("ledger.sales")) ?? [])[0]?.cancelled).toBe(true);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));
    expect(screen.getByText("This sale was cancelled.")).toBeTruthy();
  });

  it("does not offer cancelling to a cashier or after part of the sale was taken back", async () => {
    const cashier = makeCoreCtx({ flags: ["refunds"], role: "cashier" });
    await sell(cashier.ctx.outbox);
    const user = userEvent.setup();
    const first = render(<CoreRefunds ctx={cashier.ctx} />);
    await user.click(await first.findByRole("button", { name: /^Open sale/ }));
    expect(screen.queryByRole("button", { name: "Cancel this sale" })).toBeNull();
    first.unmount();

    const manager = makeCoreCtx({ flags: ["refunds"], role: "manager" });
    const sale = aSale();
    await queueSale(manager.outbox, sale, new Date());
    await manager.outbox.add({ id: newId(5), kind: "refund", record: { saleId: sale.id, lines: [{ productId: "p-bread", quantity: 1 }], refundedAt: new Date().toISOString() }, state: "pending", attempts: 0, queuedAt: new Date().toISOString() });
    render(<CoreRefunds ctx={manager.ctx} />);
    await user.click(await screen.findByRole("button", { name: /^Open sale/ }));

    expect(screen.getByText("Part of this sale was already taken back, so it cannot be cancelled.")).toBeTruthy();
  });
});

describe("approvalThreshold", () => {
  it("reads the pack setting and falls back to R500 like the server", () => {
    expect(approvalThreshold(JSON.stringify({ approvalThresholdCents: 2500 }))).toBe(2500);
    expect(approvalThreshold(null)).toBe(50_000);
    expect(approvalThreshold("nonsense")).toBe(50_000);
    expect(approvalThreshold(JSON.stringify({ approvalThresholdCents: -1 }))).toBe(50_000);
  });
});
