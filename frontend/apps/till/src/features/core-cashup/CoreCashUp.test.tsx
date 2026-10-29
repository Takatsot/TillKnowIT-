// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { creditPaymentRecord, newId, queueBuilt, queueSale, type RecordOutboxEntry } from "@tillknowit/domain";
import { afterEach, describe, expect, it } from "vitest";
import { aSale, makeCoreCtx, STAFF } from "../../coreTesting";
import { CoreCashUp } from "./CoreCashUp";

afterEach(cleanup);

const cashUps = async (outbox: { all: () => Promise<unknown[]> }) => ((await outbox.all()) as RecordOutboxEntry[]).filter((e) => e.kind === "cash_up").map((e) => e.record as Record<string, unknown>);

describe("Cash-up tab", () => {
  it("works the expected cash out of the till's own books and shows the difference as the cashier types", async () => {
    const { ctx, outbox } = makeCoreCtx();
    await queueSale(outbox, aSale({ id: newId(1) }), new Date());   // R36 in cash
    await queueSale(outbox, aSale({ id: newId(2), paymentMethodCode: "card", tenderedCents: null, changeCents: null }), new Date());   // card: not in the drawer
    const cancelled = aSale({ id: newId(3) });
    await queueSale(outbox, cancelled, new Date());   // cash, then cancelled
    await outbox.add({ id: newId(4), kind: "cancellation", record: { saleId: cancelled.id }, state: "pending", attempts: 0, queuedAt: new Date().toISOString() });
    const payment = creditPaymentRecord({ accountId: "acc", amountCents: 2000, paymentMethodCode: "cash" }, { id: newId(5), staffId: STAFF, at: new Date() });
    await queueBuilt(outbox, "credit_entry", payment, new Date());
    const user = userEvent.setup();
    render(<CoreCashUp ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: /^Float/ }), "500");
    const expected = (await screen.findByText("Expected in the drawer")).closest("tr")!;
    await waitFor(() => expect(expected.textContent).toContain("556"));   // 500 float + 36 cash sale + 20 credit payment
    await user.type(screen.getByRole("textbox", { name: "Cash counted in the drawer" }), "550");

    expect(screen.getByText(/Short by/).textContent).toContain("6");
  });

  it("saves the cash-up with the float, the expected and the counted cash, and nudges the sync", async () => {
    const { ctx, outbox, nudgeSync, kv } = makeCoreCtx();
    await queueSale(outbox, aSale(), new Date());
    const user = userEvent.setup();
    render(<CoreCashUp ctx={ctx} />);
    await user.type(screen.getByRole("textbox", { name: /^Float/ }), "100");
    await user.type(screen.getByRole("textbox", { name: "Cash counted in the drawer" }), "136");
    await user.type(screen.getByRole("textbox", { name: "Note (optional)" }), "all good");

    await user.click(screen.getByRole("button", { name: "Save cash-up" }));

    await waitFor(() => expect(screen.getByText(/Cash-up saved for .* The drawer balances\./)).toBeTruthy());
    expect(await cashUps(outbox)).toEqual([expect.objectContaining({ staffId: STAFF, floatCents: 10_000, expectedCents: 13_600, countedCents: 13_600, note: "all good" })]);
    expect(nudgeSync).toHaveBeenCalled();
    expect(await kv.get("cashup.float")).toBe(10_000);
  });

  it("warns when a cash-up was already saved today and reuses the last float", async () => {
    const { ctx } = makeCoreCtx();
    const user = userEvent.setup();
    const first = render(<CoreCashUp ctx={ctx} />);
    await user.type(screen.getByRole("textbox", { name: /^Float/ }), "250");
    await user.type(screen.getByRole("textbox", { name: "Cash counted in the drawer" }), "250");
    await user.click(screen.getByRole("button", { name: "Save cash-up" }));
    await waitFor(() => expect(screen.getByText(/Cash-up saved/)).toBeTruthy());
    first.unmount();

    render(<CoreCashUp ctx={ctx} />);

    expect(await screen.findByText(/was already saved from this till/)).toBeTruthy();
    await waitFor(() => expect((screen.getByRole("textbox", { name: /^Float/ }) as HTMLInputElement).value).toBe("250.00"));
  });

  it("cannot be saved until the counted cash is typed as an amount", async () => {
    const { ctx, outbox } = makeCoreCtx();
    const user = userEvent.setup();
    render(<CoreCashUp ctx={ctx} />);
    const save = screen.getByRole("button", { name: "Save cash-up" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);

    await user.type(screen.getByRole("textbox", { name: "Cash counted in the drawer" }), "abc");

    expect(save.disabled).toBe(true);
    expect(await cashUps(outbox)).toEqual([]);
  });
});
