// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fail, newId, ok, queueBuilt, creditSaleRecord, type RecordOutboxEntry } from "@tillknowit/domain";
import { afterEach, describe, expect, it, vi } from "vitest";
import { makeCoreCtx, SHOP, STAFF } from "../../coreTesting";
import type { CreditAccount } from "../../creditAccounts";
import { cashUpFigures } from "../../ledger";
import { CoreCredit } from "./CoreCredit";

afterEach(cleanup);

const nomsa: CreditAccount = { id: "acc-1", name: "Nomsa", phone: "0821234567", creditLimitCents: 50_000, balanceCents: 12_500, overLimit: false };
const peter: CreditAccount = { id: "acc-2", name: "Peter", phone: null, creditLimitCents: null, balanceCents: 0, overLimit: false };
const api = (accounts: CreditAccount[]) => ({ get: vi.fn(async (path: string) => (path === `/shops/${SHOP}/credit-accounts` ? ok(accounts) : fail("x", "unexpected"))) }) as never;
const entries = async (outbox: { all: () => Promise<unknown[]> }) => ((await outbox.all()) as RecordOutboxEntry[]).filter((e) => e.kind === "credit_entry").map((e) => e.record as Record<string, unknown>);

describe("Credit book tab", () => {
  it("lists the customers with what they owe, and finds one by name", async () => {
    const { ctx } = makeCoreCtx({ api: api([nomsa, peter]) });
    const user = userEvent.setup();
    render(<CoreCredit ctx={ctx} />);

    expect(await screen.findByText("Nomsa")).toBeTruthy();
    await user.type(screen.getByRole("textbox", { name: "Find a customer by name or phone" }), "pet");
    expect(screen.queryByText("Nomsa")).toBeNull();
    expect((screen.getByRole("button", { name: "Take payment from Peter" }) as HTMLButtonElement).disabled).toBe(true);   // owes nothing
  });

  it("takes a cash payment, queues it as a negative credit entry, and the till's books count the cash", async () => {
    const { ctx, outbox, kv, nudgeSync } = makeCoreCtx({ api: api([nomsa]) });
    const user = userEvent.setup();
    render(<CoreCredit ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: "Take payment from Nomsa" }));
    expect(screen.getByText(/Nomsa owes .*125/)).toBeTruthy();

    const amount = screen.getByRole("textbox", { name: "Amount paid" });
    await user.clear(amount);
    await user.type(amount, "50");
    await user.click(screen.getByRole("button", { name: "Save payment" }));

    await waitFor(() => expect(screen.getByText(/Payment of .*50.* from Nomsa saved\. They now owe .*75/)).toBeTruthy());
    expect(await entries(outbox)).toEqual([expect.objectContaining({ type: "payment", accountId: "acc-1", amountCents: -5000, paymentMethodCode: "cash", saleId: null, staffId: STAFF })]);
    expect(nudgeSync).toHaveBeenCalled();
    const figures = await cashUpFigures(kv, new Date().toISOString().slice(0, 10), 0, new Date(Date.now() + 1000));
    expect(figures.creditCashCents).toBe(5000);
    // The list shows the new balance at once, though the server has not heard yet.
    expect(screen.getAllByRole("row")[1]!.textContent).toContain("75");
  });

  it("does not count a card payment as cash in the drawer", async () => {
    const { ctx, kv } = makeCoreCtx({ api: api([nomsa]) });
    const user = userEvent.setup();
    render(<CoreCredit ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: "Take payment from Nomsa" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Paid by" }), "card");

    await user.click(screen.getByRole("button", { name: "Save payment" }));

    await waitFor(() => expect(screen.getByText(/saved/)).toBeTruthy());
    const figures = await cashUpFigures(kv, new Date().toISOString().slice(0, 10), 0, new Date(Date.now() + 1000));
    expect(figures.creditCashCents).toBe(0);
  });

  it("refuses more than the customer owes, and nonsense", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: api([nomsa]) });
    const user = userEvent.setup();
    render(<CoreCredit ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: "Take payment from Nomsa" }));
    const amount = screen.getByRole("textbox", { name: "Amount paid" });

    await user.clear(amount);
    await user.type(amount, "500");
    await user.click(screen.getByRole("button", { name: "Save payment" }));
    expect(screen.getByRole("alert").textContent).toContain("more than Nomsa owes");

    await user.clear(amount);
    await user.type(amount, "abc");
    await user.click(screen.getByRole("button", { name: "Save payment" }));
    expect(screen.getByRole("alert").textContent).toBe("Type the amount as a number, for example 50 or 25,50.");
    expect(await entries(outbox)).toEqual([]);
  });

  it("adds what is still in the queue to the balance, such as a credit sale not sent yet", async () => {
    const { ctx, outbox } = makeCoreCtx({ api: api([nomsa]) });
    await queueBuilt(outbox, "credit_entry", creditSaleRecord({ accountId: "acc-1", saleId: "s1", totalCents: 2500 }, { id: newId(1), staffId: STAFF, at: new Date() }), new Date());
    render(<CoreCredit ctx={ctx} />);

    await screen.findByText("Nomsa");

    expect(screen.getAllByRole("row")[1]!.textContent).toContain("150");   // 125 + 25
  });

  it("shows the kept copy when offline and when the server cannot be reached", async () => {
    const offline = makeCoreCtx({ online: false });
    await offline.kv.set("creditAccounts", [nomsa]);
    const first = render(<CoreCredit ctx={offline.ctx} />);
    expect(await screen.findByText("Nomsa")).toBeTruthy();
    expect(screen.getByText(/The till is offline, so these are the balances/)).toBeTruthy();
    first.unmount();

    const down = makeCoreCtx({ api: { get: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) } as never });
    await down.kv.set("creditAccounts", [nomsa]);
    render(<CoreCredit ctx={down.ctx} />);
    expect(await screen.findByText("Nomsa")).toBeTruthy();
    expect(screen.getByText("The server could not be reached, so this is the copy kept on the till.")).toBeTruthy();
  });

  it("says so when there are no credit customers", async () => {
    const { ctx } = makeCoreCtx({ api: api([]) });
    render(<CoreCredit ctx={ctx} />);

    expect(await screen.findByText(/no credit customers on this till yet/)).toBeTruthy();
  });
});
