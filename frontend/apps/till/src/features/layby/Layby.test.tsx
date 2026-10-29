// @vitest-environment jsdom
import { fail, isSaleEntry, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { LaybyTab } from "./Layby";
import { cancellationFee, minimumDeposit, parseTerms } from "./terms";

afterEach(cleanup);

const line = { productId: "p-bread", name: "Bread", quantity: 10, priceCents: 1800, unitDivisor: 1 };
const plan = (over = {}) => ({
  id: "l1", reference: "L-1", customerName: "Anele", customerPhone: null, lines: [line], totalCents: 18000, paidCents: 5000, balanceCents: 13000, dueOn: "2026-12-01", status: "active",
  cancellationFeeCents: 0, refundDueCents: 0, payments: [{ amountCents: 5000, methodCode: "cash", paidAt: "2026-10-26T08:00:00Z" }], ...over,
});
const settings = { flag: "layby", settingsJson: JSON.stringify({ minDepositPercent: 30, maxWeeks: 8, cancelFeePercent: 10, minCancelFeeCents: 2000 }) };

describe("lay-by terms", () => {
  it("reads the pack settings and falls back to the server's defaults", () => {
    expect(parseTerms(settings.settingsJson)).toEqual({ minDepositPercent: 30, maxWeeks: 8, cancelFeePercent: 10, minCancelFeeCents: 2000 });
    expect(parseTerms(null).minDepositPercent).toBe(20);
    expect(parseTerms("not json").maxWeeks).toBe(12);
  });

  it("rounds the deposit up and caps the cancellation fee at what was paid", () => {
    expect(minimumDeposit(1001, parseTerms(null))).toBe(201);
    expect(cancellationFee(18000, 5000, parseTerms(null))).toBe(2000);
    expect(cancellationFee(18000, 1000, parseTerms(null))).toBe(1000);
  });
});

describe("layby feature", () => {
  it("is switched on by the layby flag", () => {
    expect(feature.flag).toBe("layby");
    expect(feature.requiresOnline).toBe(true);
  });

  it("shows the empty state", async () => {
    const { ctx } = makeCtx({ flags: ["layby"], api: { get: vi.fn(async () => ok([])) as never } });
    render(<LaybyTab ctx={ctx} />);

    expect(await screen.findByText("No lay-bys yet.")).toBeTruthy();
  });

  it("starts a lay-by with the deposit the shop's terms ask for", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(plan()));
    const { ctx } = makeCtx({ flags: [settings], api: { get: vi.fn(async () => ok([])), post } as never });
    render(<LaybyTab ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Start a lay-by" }));
    expect(screen.getByText("The deposit is at least 30% of the total. The lay-by must be paid off within 8 weeks.")).toBeTruthy();
    await user.type(screen.getByRole("textbox", { name: "Customer name" }), "Anele");
    await user.type(screen.getByRole("textbox", { name: "Search for an item" }), "bread");
    await user.click(screen.getByRole("button", { name: "Add Bread" }));

    await user.type(screen.getByRole("textbox", { name: "Deposit" }), "2");
    await user.click(screen.getByRole("button", { name: "Start lay-by" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/at least R\s?5,40/);
    expect(post).not.toHaveBeenCalled();

    await user.clear(screen.getByRole("textbox", { name: "Deposit" }));
    await user.type(screen.getByRole("textbox", { name: "Deposit" }), "10");
    await user.click(screen.getByRole("button", { name: "Start lay-by" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF, customerName: "Anele", depositCents: 1000, depositMethod: "cash" });
    expect(await screen.findByText(/Lay-by L-1 started/)).toBeTruthy();
  });

  it("takes a payment and refuses more than is owing", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(plan({ paidCents: 8000, balanceCents: 10000 })));
    const { ctx } = makeCtx({ flags: ["layby"], api: { get: vi.fn(async () => ok([plan()])), post } as never });
    render(<LaybyTab ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Open L-1" }));
    await user.type(screen.getByRole("textbox", { name: "Payment" }), "500");
    await user.click(screen.getByRole("button", { name: "Take payment" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/Only R\s?130,00 is owing/);

    await user.clear(screen.getByRole("textbox", { name: "Payment" }));
    await user.type(screen.getByRole("textbox", { name: "Payment" }), "30");
    await user.click(screen.getByRole("button", { name: "Take payment" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(expect.stringContaining("/laybys/l1/payments"), { deviceId: DEVICE, staffId: STAFF, amountCents: 3000, methodCode: "cash" }));
    expect(await screen.findByText(/Payment of R\s?30,00 taken/)).toBeTruthy();
  });

  it("shows the cancellation fee before cancelling and the server's amounts after", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => ok(plan({ status: "cancelled", balanceCents: 0, cancellationFeeCents: 2000, refundDueCents: 3000 })));
    const { ctx } = makeCtx({ flags: ["layby"], api: { get: vi.fn(async () => ok([plan()])), post } as never });
    render(<LaybyTab ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Open L-1" }));
    await user.click(screen.getByRole("button", { name: "Cancel lay-by" }));
    expect(screen.getByText(/the shop keeps about R\s?20,00 and R\s?30,00 goes back/)).toBeTruthy();
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, cancel the lay-by" }));

    expect(await screen.findByText(/Lay-by cancelled. The shop keeps R\s?20,00. R\s?30,00 goes back/)).toBeTruthy();
  });

  it("collects a paid-off lay-by as a sale paid by lay-by, and waits if the server has not got the sale yet", async () => {
    const user = userEvent.setup();
    const done = plan({ status: "completed", paidCents: 18000, balanceCents: 0 });
    const post = vi.fn()
      .mockResolvedValueOnce(fail("layby.sale_unknown", "Send the sale to the server first."))
      .mockResolvedValueOnce(ok(plan({ status: "collected", paidCents: 18000, balanceCents: 0 })));
    const { ctx, outbox } = makeCtx({ flags: ["layby"], api: { get: vi.fn(async () => ok([done])), post } as never });
    render(<LaybyTab ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Open L-1" }));
    await user.click(screen.getByRole("button", { name: "Collect the goods" }));
    expect(await screen.findByText(/Press Collect again in a moment/)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Collect the goods" }));
    expect(await screen.findByText("The goods were collected and rung up as a sale.")).toBeTruthy();

    const sales = (await outbox.all()).filter(isSaleEntry);
    expect(sales).toHaveLength(1);   // pressing twice is one sale
    expect(sales[0]!.sale).toMatchObject({ paymentMethodCode: "layby", totalCents: 18000 });
    const ids = post.mock.calls.map((c) => (c[1] as { saleId: string }).saleId);
    expect(new Set(ids).size).toBe(1);
  });
});
