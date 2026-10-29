// @vitest-environment jsdom
import type { Api } from "@tillknowit/api-client";
import { fail, MemoryOutbox, ok, type CatalogueItem, type RecordOutboxEntry, type SaleOutboxEntry } from "@tillknowit/domain";
import { FakeCashDrawer, FakePrinter, FakeScale } from "@tillknowit/hardware";
import { Features, type ShopPack } from "@tillknowit/packs";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { CreditAccount } from "./creditAccounts";
import type { SaleBridge, TillHardware } from "./feature";
import { SellScreen } from "./SellScreen";
import type { StaffRecord } from "./staffStore";

afterEach(cleanup);

const staff: StaffRecord = { id: "0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b", name: "Thandi", role: "cashier", pinHash: "x" };
const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
const mince: CatalogueItem = { id: "p-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true };
const now = new Date("2026-10-26T08:00:00Z");
const nomsa: CreditAccount = { id: "acc-1", name: "Nomsa", phone: "0821234567", creditLimitCents: 2_000, balanceCents: 1_500, overLimit: false };

interface Setup {
  flags?: string[];
  hardware?: TillHardware;
  api?: unknown;
  online?: boolean;
  creditAccounts?: CreditAccount[];
  onBridge?: (b: SaleBridge | null) => void;
}

function setup(over: Setup = {}) {
  const pack: ShopPack = { shopId: SHOP, packCode: "t", packName: "T", packVersion: 1, slipTemplateCode: "slip.sale", features: (over.flags ?? []).map((flag) => ({ flag })) };
  const outbox = new MemoryOutbox();
  const user = userEvent.setup();
  render(
    <SellScreen
      staff={staff} language="en" catalogue={[bread, mince]} promotions={[]} barcodeRules={null} outbox={outbox} clock={() => now} features={new Features(pack)}
      hardware={over.hardware} api={over.api as Api | undefined} device={{ shopId: SHOP, deviceId: DEVICE }} online={over.online ?? true} creditAccounts={over.creditAccounts}
      shopName="Corner Shop" onBridge={over.onBridge} weighOptions={{ delay: async () => {} }}
    />,
  );
  return { outbox, user };
}

const entry = () => screen.getByRole("textbox", { name: "Scan or type a barcode or name" });
const scanBread = async (user: ReturnType<typeof userEvent.setup>, times = 1) => {
  for (let i = 0; i < times; i++) await user.type(entry(), "6001000000011{Enter}");
};
const queuedSales = async (outbox: MemoryOutbox) => ((await outbox.all()).filter((e) => !("kind" in e) || e.kind === undefined || e.kind === "sale") as SaleOutboxEntry[]).map((e) => e.sale);
const records = async (outbox: MemoryOutbox, kind: string) => ((await outbox.all()) as RecordOutboxEntry[]).filter((e) => e.kind === kind).map((e) => e.record as Record<string, unknown>);
const pay = (user: ReturnType<typeof userEvent.setup>) => user.click(screen.getByRole("button", { name: /^Pay/ }));

describe("tips", () => {
  it("is not offered unless the pack has tips on", async () => {
    const { user } = setup();
    await scanBread(user);

    expect(screen.queryByRole("group", { name: "Tip" })).toBeNull();
  });

  it("adds a percentage tip on top of the bill, shows what is to be paid, and sends the tip with the sale", async () => {
    const { user, outbox } = setup({ flags: ["tips"] });
    await scanBread(user);

    await user.click(screen.getByRole("button", { name: "Tip 10%" }));
    expect(screen.getByText("To pay")).toBeTruthy();
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Cash" }));
    await user.click(screen.getByRole("button", { name: "Exact" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale).toMatchObject({ totalCents: 1800, tipCents: 180, tenderedCents: 1980, changeCents: 0 });
  });

  it("takes a tip as an amount and refuses one bigger than the bill", async () => {
    const { user } = setup({ flags: ["tips"] });
    await scanBread(user);

    await user.type(screen.getByRole("textbox", { name: "Tip amount" }), "25");
    await user.click(screen.getByRole("button", { name: "Set tip" }));
    expect(screen.getByRole("status").textContent).toBe("A tip cannot be more than the bill.");
    expect(screen.queryByText("To pay")).toBeNull();

    await user.clear(screen.getByRole("textbox", { name: "Tip amount" }));
    await user.type(screen.getByRole("textbox", { name: "Tip amount" }), "7,50");
    await user.click(screen.getByRole("button", { name: "Set tip" }));
    expect(screen.getByText("To pay")).toBeTruthy();

    await user.clear(screen.getByRole("textbox", { name: "Tip amount" }));
    await user.type(screen.getByRole("textbox", { name: "Tip amount" }), "abc");
    await user.click(screen.getByRole("button", { name: "Set tip" }));
    expect(screen.getByRole("status").textContent).toContain("Type the tip as an amount");
  });

  it("can be taken off again", async () => {
    const { user } = setup({ flags: ["tips"] });
    await scanBread(user);
    await user.click(screen.getByRole("button", { name: "Tip 15%" }));

    await user.click(screen.getByRole("button", { name: "No tip" }));

    expect(screen.queryByText("To pay")).toBeNull();
  });
});

describe("split payments", () => {
  async function toSplit(user: ReturnType<typeof userEvent.setup>) {
    await scanBread(user, 2);   // R36
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Split payment" }));
  }

  it("is only offered when the pack has split payments on", async () => {
    const { user } = setup();
    await scanBread(user);
    await pay(user);

    expect(screen.queryByRole("button", { name: "Split payment" })).toBeNull();
  });

  it("splits between cash and card, works out the change on the cash part, and queues one split sale", async () => {
    const { user, outbox } = setup({ flags: ["split_payments"] });
    await toSplit(user);

    expect((screen.getByRole("button", { name: "Pay" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByRole("textbox", { name: "Amount for part 1" }), "10");
    await user.type(screen.getByRole("textbox", { name: "Cash received for part 1" }), "20");
    expect(screen.getByText(/Still to pay: .*26/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Add a part" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Method for part 2" }), "card");
    expect((screen.getByRole("textbox", { name: "Amount for part 2" }) as HTMLInputElement).value).toBe("26.00");
    await user.click(screen.getByRole("button", { name: "Pay" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale).toMatchObject({
      paymentMethodCode: "split", totalCents: 3600, tenderedCents: null, changeCents: null, accountId: null,
      payments: [{ methodCode: "cash", amountCents: 1000, tenderedCents: 2000 }, { methodCode: "card", amountCents: 2600, reference: null, tenderedCents: null }],
    });
    expect(screen.getByRole("status").textContent).toContain("Sale saved");
  });

  it("needs a reference for a wallet or voucher part and will not pay until the parts add up to the total", async () => {
    const { user, outbox } = setup({ flags: ["split_payments"] });
    await toSplit(user);
    await user.selectOptions(screen.getByRole("combobox", { name: "Method for part 1" }), "voucher");
    await user.type(screen.getByRole("textbox", { name: "Amount for part 1" }), "36");
    const payButton = screen.getByRole("button", { name: "Pay" }) as HTMLButtonElement;

    expect(payButton.disabled).toBe(true);   // no reference yet
    await user.type(screen.getByRole("textbox", { name: "Reference for part 1" }), "V-123");
    expect(payButton.disabled).toBe(false);
    await user.clear(screen.getByRole("textbox", { name: "Amount for part 1" }));
    await user.type(screen.getByRole("textbox", { name: "Amount for part 1" }), "30");
    expect(payButton.disabled).toBe(true);
    expect(screen.getByText(/Still to pay/)).toBeTruthy();
    await user.clear(screen.getByRole("textbox", { name: "Amount for part 1" }));
    await user.type(screen.getByRole("textbox", { name: "Amount for part 1" }), "40");
    expect(screen.getByText(/Over by/)).toBeTruthy();
    expect(await queuedSales(outbox)).toEqual([]);
  });

  it("never offers the same method twice, and cannot add a part when every method is used", async () => {
    const { user } = setup({ flags: ["split_payments"] });
    await toSplit(user);
    for (let i = 0; i < 5; i++) await user.click(screen.getByRole("button", { name: "Add a part" }));

    expect(screen.getAllByRole("combobox").length).toBe(6);
    expect((screen.getByRole("button", { name: "Add a part" }) as HTMLButtonElement).disabled).toBe(true);
    const methods = screen.getAllByRole("combobox").map((s) => (s as HTMLSelectElement).value);
    expect(new Set(methods).size).toBe(6);
    expect(screen.queryByRole("option", { name: "Credit" })).toBeNull();   // credit is never part of a split
  });

  it("can remove a part", async () => {
    const { user } = setup({ flags: ["split_payments"] });
    await toSplit(user);
    await user.click(screen.getByRole("button", { name: "Add a part" }));

    await user.click(screen.getByRole("button", { name: "Remove part 1" }));

    expect(screen.getAllByRole("combobox").length).toBe(1);
  });
});

describe("credit sales", () => {
  it("sells on credit to a chosen customer, queues the sale and the customer's credit entry, and warns about the limit without blocking", async () => {
    const { user, outbox } = setup({ creditAccounts: [nomsa] });
    await scanBread(user);   // R18 on top of R15 owed against a R20 limit
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Credit" }));

    expect(screen.getByText("This sale takes Nomsa over their credit limit. You can still go ahead.")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Nomsa/ }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale).toMatchObject({ paymentMethodCode: "credit", accountId: "acc-1", totalCents: 1800, tenderedCents: null });
    const [entry] = await records(outbox, "credit_entry");
    expect(entry).toMatchObject({ type: "sale", accountId: "acc-1", amountCents: 1800, saleId: sale!.id, staffId: staff.id });
  });

  it("queues the sale before its credit entry, so they arrive in that order", async () => {
    const { user, outbox } = setup({ creditAccounts: [nomsa] });
    await scanBread(user);
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Credit" }));
    await user.click(screen.getByRole("button", { name: /Nomsa/ }));

    await waitFor(async () => expect((await outbox.all()).length).toBe(2));
    const all = await outbox.all();
    expect(all.map((e) => e.kind ?? "sale")).toEqual(["sale", "credit_entry"]);
  });

  it("says so when there are no credit customers", async () => {
    const { user } = setup();
    await scanBread(user);
    await pay(user);

    await user.click(screen.getByRole("button", { name: "Credit" }));

    expect(screen.getByText(/no credit customers on this till yet/)).toBeTruthy();
  });

  it("will not mix a tip with credit", async () => {
    const { user, outbox } = setup({ flags: ["tips"], creditAccounts: [nomsa] });
    await scanBread(user);
    await user.click(screen.getByRole("button", { name: "Tip 10%" }));
    await pay(user);

    await user.click(screen.getByRole("button", { name: "Credit" }));

    expect(screen.getByText("Take the tip off to sell on credit.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Nomsa/ })).toBeNull();
    expect(await queuedSales(outbox)).toEqual([]);
  });
});

describe("customers", () => {
  const customers = [{ id: "cust-1", name: "Lerato", phone: "+27821234567" }];

  it("finds a customer by phone and sends the sale with the customer on it", async () => {
    const get = vi.fn(async () => ok(customers));
    const { user, outbox } = setup({ flags: ["customers"], api: { get } });
    await scanBread(user);

    await user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "0821234567");
    await user.click(screen.getByRole("button", { name: "Find customer" }));
    expect(await screen.findByText("Customer: Lerato")).toBeTruthy();
    expect(get).toHaveBeenCalledWith(`/shops/${SHOP}/customers`, { search: "0821234567", deviceId: DEVICE, staffId: staff.id });
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    expect((await queuedSales(outbox))[0]).toMatchObject({ customerId: "cust-1" });
  });

  it("can remove the customer, and says when nobody has that number or the till is offline", async () => {
    const get = vi.fn(async () => ok([] as typeof customers));
    const first = setup({ flags: ["customers"], api: { get } });
    await scanBread(first.user);
    await first.user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "0829999999");
    await first.user.click(screen.getByRole("button", { name: "Find customer" }));
    expect(await screen.findByText("No customer has that phone number.")).toBeTruthy();
    cleanup();

    const offline = setup({ flags: ["customers"], api: { get }, online: false });
    await scanBread(offline.user);
    await offline.user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "0829999999");
    await offline.user.click(screen.getByRole("button", { name: "Find customer" }));
    expect(screen.getByRole("status").textContent).toBe("Customers can only be found when the till is online.");
    cleanup();

    const found = setup({ flags: ["customers"], api: { get: vi.fn(async () => ok(customers)) } });
    await scanBread(found.user);
    await found.user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "0821234567");
    await found.user.click(screen.getByRole("button", { name: "Find customer" }));
    await found.user.click(await screen.findByRole("button", { name: "Remove customer" }));
    expect(screen.getByRole("textbox", { name: "Customer phone number" })).toBeTruthy();
  });

  it("lets the person choose when several customers match, and shows the server's refusal", async () => {
    const two = [...customers, { id: "cust-2", name: "Lerato M", phone: "+27829999999" }];
    const first = setup({ flags: ["customers"], api: { get: vi.fn(async () => ok(two)) } });
    await scanBread(first.user);
    await first.user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "082");
    await first.user.click(screen.getByRole("button", { name: "Find customer" }));
    await first.user.click(await screen.findByRole("button", { name: /Lerato M/ }));
    expect(screen.getByText("Customer: Lerato M")).toBeTruthy();
    cleanup();

    const refused = setup({ flags: ["customers"], api: { get: vi.fn(async () => fail("permission.denied", "You may not look up customers.")) } });
    await scanBread(refused.user);
    await refused.user.type(screen.getByRole("textbox", { name: "Customer phone number" }), "082");
    await refused.user.click(screen.getByRole("button", { name: "Find customer" }));
    expect(await screen.findByText("You may not look up customers.")).toBeTruthy();
  });
});

describe("the scale", () => {
  it("reads a steady weight into the weight box", async () => {
    const scale = new FakeScale([{ grams: 1200, stable: false }, { grams: 1250, stable: true }, { grams: 1251, stable: true }]);
    const { user, outbox } = setup({ hardware: { scale } });
    await user.type(entry(), "123456{Enter}");

    await user.click(screen.getByRole("button", { name: "Read from scale" }));

    await waitFor(() => expect((screen.getByRole("textbox", { name: "Weight of Beef mince in grams" }) as HTMLInputElement).value).toBe("1251"));
    await user.click(screen.getByRole("button", { name: "Add weight" }));
    expect(screen.getByText("1251 g")).toBeTruthy();
    expect(await queuedSales(outbox)).toEqual([]);
  });

  it("tells the cashier in plain words when the weight is not steady or the scale does not answer", async () => {
    const wobbling = new FakeScale([{ grams: 100, stable: false }]);
    const first = setup({ hardware: { scale: wobbling } });
    await first.user.type(entry(), "123456{Enter}");
    await first.user.click(screen.getByRole("button", { name: "Read from scale" }));
    expect((await screen.findByRole("status")).textContent).toBe("The weight is not steady yet. Wait a moment and try again.");
    cleanup();

    const dead = new FakeScale([]);
    dead.offline = true;
    const second = setup({ hardware: { scale: dead } });
    await second.user.type(entry(), "123456{Enter}");
    await second.user.click(screen.getByRole("button", { name: "Read from scale" }));
    expect((await screen.findByRole("status")).textContent).toBe("The scale is not answering. Check it is on and connected.");
  });

  it("offers no scale button when there is no scale, and weight is typed by hand", async () => {
    const { user } = setup();
    await user.type(entry(), "123456{Enter}");

    expect(screen.queryByRole("button", { name: "Read from scale" })).toBeNull();
    await user.type(screen.getByRole("textbox", { name: "Weight of Beef mince in grams" }), "500");
    await user.click(screen.getByRole("button", { name: "Add weight" }));
    expect(screen.getByText("500 g")).toBeTruthy();
  });
});

describe("the slip and the cash drawer", () => {
  const text = (job: Uint8Array) => new TextDecoder("latin1").decode(job);

  async function sellFor(user: ReturnType<typeof userEvent.setup>, method: "Cash" | "Card") {
    await scanBread(user);
    await pay(user);
    if (method === "Cash") {
      await user.click(screen.getByRole("button", { name: "Cash" }));
      await user.click(screen.getByRole("button", { name: "Exact" }));
    } else await user.click(screen.getByRole("button", { name: "Card" }));
  }

  it("prints the slip and opens the drawer after a cash sale when the pack has both and the devices are there", async () => {
    const printer = new FakePrinter();
    const drawer = new FakeCashDrawer();
    const { user } = setup({ flags: ["printer", "cash_drawer"], hardware: { printer, drawer } });

    await sellFor(user, "Cash");

    await waitFor(() => expect(printer.jobs.length).toBe(1));
    expect(drawer.opened).toBe(1);
    expect(text(printer.jobs[0]!)).toContain("Corner Shop");
    expect(text(printer.jobs[0]!)).toContain("Bread");
    expect(text(printer.jobs[0]!)).toContain("Paid by");
    expect(text(printer.jobs[0]!)).toContain("Cash");
  });

  it("does not open the drawer for a card sale", async () => {
    const printer = new FakePrinter();
    const drawer = new FakeCashDrawer();
    const { user } = setup({ flags: ["printer", "cash_drawer"], hardware: { printer, drawer } });

    await sellFor(user, "Card");

    await waitFor(() => expect(printer.jobs.length).toBe(1));
    expect(drawer.opened).toBe(0);
  });

  it("lets the printer open the drawer when there is no separate drawer", async () => {
    const withKick = new FakePrinter();
    const plain = new FakePrinter();
    const first = setup({ flags: ["printer", "cash_drawer"], hardware: { printer: withKick } });
    await sellFor(first.user, "Cash");
    await waitFor(() => expect(withKick.jobs.length).toBe(1));
    cleanup();
    const second = setup({ flags: ["printer"], hardware: { printer: plain } });
    await sellFor(second.user, "Cash");
    await waitFor(() => expect(plain.jobs.length).toBe(1));

    expect(withKick.jobs[0]!.length).toBeGreaterThan(plain.jobs[0]!.length);   // the drawer command is part of the job
  });

  it("prints and opens nothing when the pack does not have the feature or the device is missing", async () => {
    const printer = new FakePrinter();
    const drawer = new FakeCashDrawer();
    const first = setup({ flags: [], hardware: { printer, drawer } });
    await sellFor(first.user, "Cash");
    await screen.findByText(/Sale saved/);
    expect(printer.jobs).toEqual([]);
    expect(drawer.opened).toBe(0);
    cleanup();

    const second = setup({ flags: ["printer", "cash_drawer"], hardware: {} });
    await sellFor(second.user, "Cash");
    const status = (await screen.findByRole("status")).textContent ?? "";
    expect(status).toMatch(/^Sale saved./);
    expect(status).not.toContain("did not");
  });

  it("keeps the sale and says in plain words why the slip did not print or the drawer did not open", async () => {
    const printer = new FakePrinter();
    printer.state = "out_of_paper";
    const drawer = new FakeCashDrawer();
    drawer.failing = true;
    const { user, outbox } = setup({ flags: ["printer", "cash_drawer"], hardware: { printer, drawer } });

    await sellFor(user, "Cash");

    await waitFor(() => expect(screen.getByRole("status").textContent).toContain("The slip did not print: The printer is out of paper. Put in a new roll."));
    expect(screen.getByRole("status").textContent).toContain("The cash drawer did not open: The cash drawer did not open. Open it with the key.");
    expect((await queuedSales(outbox)).length).toBe(1);
  });

  it("says the QR slip comes after sync while the sale is still in the queue, and does not ask the server", async () => {
    const post = vi.fn();
    const { user } = setup({ api: { post } });
    await sellFor(user, "Card");

    await user.click(screen.getByRole("button", { name: "Get QR slip" }));

    expect(await screen.findByText("The QR slip will be available once this sale has been sent to the server.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();
  });

  it("gets the QR slip once the sale has synced, shows its link and prints it with the QR code", async () => {
    const post = vi.fn(async () => ok({ status: "shown", url: "https://slips.example/s/abc" }));
    const printer = new FakePrinter();
    const { user, outbox } = setup({ flags: ["printer"], hardware: { printer }, api: { post } });
    await sellFor(user, "Card");
    await waitFor(() => expect(printer.jobs.length).toBe(1));
    const [queued] = await outbox.all();
    await outbox.remove(queued!.id);   // the sync has sent it

    await user.click(screen.getByRole("button", { name: "Get QR slip" }));

    expect(await screen.findByText("QR slip: https://slips.example/s/abc")).toBeTruthy();
    expect(post).toHaveBeenCalledWith(`/sales/${queued!.id}/slip`, { shopId: SHOP, deviceId: DEVICE, staffId: staff.id, channel: "qr", destination: null, language: "en" });
    await user.click(screen.getByRole("button", { name: "Print slip with QR" }));
    await waitFor(() => expect(printer.jobs.length).toBe(2));
    expect(printer.jobs[1]!.length).toBeGreaterThan(printer.jobs[0]!.length);
  });

  it("shows the server's words when it cannot make the slip", async () => {
    const post = vi.fn(async () => fail("slip.limit_reached", "Too many slips today."));
    const { user, outbox } = setup({ api: { post } });
    await sellFor(user, "Card");
    const [queued] = await outbox.all();
    await outbox.remove(queued!.id);

    await user.click(screen.getByRole("button", { name: "Get QR slip" }));

    expect(await screen.findByText("Too many slips today.")).toBeTruthy();
  });

  it("says to connect when the sale has synced but the till is offline", async () => {
    const post = vi.fn();
    const { user, outbox } = setup({ api: { post }, online: false });
    await sellFor(user, "Card");
    const [queued] = await outbox.all();
    await outbox.remove(queued!.id);

    await user.click(screen.getByRole("button", { name: "Get QR slip" }));

    expect(await screen.findByText("Connect the till to the internet to get the QR slip.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();
  });
});

describe("selling things from other tabs", () => {
  function withBridge() {
    let bridge: SaleBridge | null = null;
    const ui = setup({ onBridge: (b) => { bridge = b; } });
    return { ...ui, bridge: () => bridge! };
  }

  it("adds a line at an agreed price that is not discounted and goes to the server as that price", async () => {
    const { user, outbox, bridge } = withBridge();

    await waitFor(() => expect(bridge()).toBeTruthy());
    const { act } = await import("@testing-library/react");
    act(() => { expect(bridge().addLine({ productId: "svc-cut", name: "Haircut", priceCents: 15_000, costCents: 2_000 })).toEqual({ ok: true, value: undefined }); });
    expect(await screen.findByText("Haircut")).toBeTruthy();
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    expect((await queuedSales(outbox))[0]!.lines).toEqual([expect.objectContaining({ productId: "svc-cut", name: "Haircut", priceCents: 15_000, costCents: 2_000, quantity: 1, discountCents: 0 })]);
  });

  it("adds a catalogue product and refuses a product it does not know", async () => {
    const { bridge } = withBridge();
    await waitFor(() => expect(bridge()).toBeTruthy());
    const { act } = await import("@testing-library/react");

    act(() => { expect(bridge().addProduct("p-bread", 2).ok).toBe(true); });
    expect(await screen.findByText("× 2")).toBeTruthy();
    act(() => { expect(bridge().addProduct("nope")).toMatchObject({ ok: false, code: "basket.unknown_item" }); });
  });

  it("works on the latest sale, not an old copy, and attaches a customer", async () => {
    const { user, outbox, bridge } = withBridge();
    await waitFor(() => expect(bridge()).toBeTruthy());
    const { act } = await import("@testing-library/react");
    await scanBread(user);

    act(() => { bridge().addLine({ productId: "svc", name: "Service", priceCents: 1000 }); });
    act(() => { bridge().addLine({ productId: "svc", name: "Service", priceCents: 500 }); });
    act(() => bridge().setCustomer("cust-9"));
    await pay(user);
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale).toMatchObject({ totalCents: 3300, customerId: "cust-9" });
    expect(sale!.lines.map((l) => [l.name, l.priceCents])).toEqual([["Bread", 1800], ["Service", 1500]]);
  });

  it("refuses to put a scanned item and an agreed price on the same product", async () => {
    const { user, bridge } = withBridge();
    await waitFor(() => expect(bridge()).toBeTruthy());
    const { act } = await import("@testing-library/react");
    await scanBread(user);

    act(() => { expect(bridge().addLine({ productId: "p-bread", name: "Bread", priceCents: 100 })).toMatchObject({ ok: false, code: "basket.label_mixed" }); });
  });
});
