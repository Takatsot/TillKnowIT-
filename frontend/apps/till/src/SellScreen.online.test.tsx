// @vitest-environment jsdom
import type { Api } from "@tillknowit/api-client";
import { fail, hashPin, MemoryOutbox, ok, type CatalogueItem, type SaleOutboxEntry } from "@tillknowit/domain";
import { Features, type ShopPack } from "@tillknowit/packs";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SaleBridge } from "./feature";
import { MemoryKeyValueStore } from "./localData";
import { adoptHeldReleases, pendingReleases, sendPendingReleases } from "./pendingReleases";
import { pendingSerials } from "./pendingSerials";
import { SellScreen } from "./SellScreen";
import { saveStaff, type StaffRecord } from "./staffStore";

beforeEach(() => localStorage.clear());
afterEach(cleanup);

const staff: StaffRecord = { id: "0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b", name: "Thandi", role: "cashier", pinHash: "x" };
const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
const phone: CatalogueItem = { id: "p-phone", name: "Phone", barcode: "6001000000099", priceCents: 50_000, costCents: 40_000, soldByWeight: false, requiresSerial: true };
const now = new Date("2026-10-26T08:00:00Z");

interface Setup {
  flags?: string[];
  api?: unknown;
  online?: boolean;
  managers?: boolean;
  onBridge?: (b: SaleBridge | null) => void;
  onSerialsQueued?: () => void;
}

async function setup(over: Setup = {}) {
  const pack: ShopPack = { shopId: SHOP, packCode: "t", packName: "T", packVersion: 1, slipTemplateCode: "slip.sale", features: (over.flags ?? []).map((flag) => ({ flag })) };
  const kv = new MemoryKeyValueStore();
  await saveStaff(kv, [staff, ...(over.managers === false ? [] : [{ id: "boss-1", name: "Sipho", role: "manager" as const, pinHash: await hashPin("4321") }])]);
  const outbox = new MemoryOutbox();
  const user = userEvent.setup();
  const screenFor = (online: boolean) => (
    <SellScreen
      staff={staff} language="en" catalogue={[bread, phone]} promotions={[]} barcodeRules={null} outbox={outbox} clock={() => now} features={new Features(pack)}
      api={over.api as Api | undefined} device={{ shopId: SHOP, deviceId: DEVICE }} online={online} kv={kv} onBridge={over.onBridge} onSerialsQueued={over.onSerialsQueued}
    />
  );
  const view = render(screenFor(over.online ?? true));
  return { outbox, user, kv, rerenderOnline: (online: boolean) => view.rerender(screenFor(online)) };
}

type User = ReturnType<typeof userEvent.setup>;
const entry = () => screen.getByRole("textbox", { name: "Scan or type a barcode or name" });
const scanBread = (user: User) => user.type(entry(), "6001000000011{Enter}");
const queuedSales = async (outbox: MemoryOutbox) => ((await outbox.all()) as SaleOutboxEntry[]).map((e) => e.sale);

async function askManager(user: User, price: string, pin: string) {
  await user.click(screen.getByRole("button", { name: "Change price: Bread" }));
  await user.type(screen.getByRole("textbox", { name: "New price for Bread" }), price);
  await user.click(screen.getByRole("button", { name: "Ask a manager" }));
  if (pin) {
    await user.type(await screen.findByLabelText("Manager PIN"), pin);
    await user.click(screen.getByRole("button", { name: "Approve" }));
  }
}

describe("a manager's price override", () => {
  it("lowers a line's price only after a manager's PIN, shows who approved, and sends the discount with the approver", async () => {
    const { user, outbox } = await setup();
    await scanBread(user);

    await askManager(user, "15", "4321");

    expect(await screen.findByText(/Price approved by Sipho/)).toBeTruthy();
    expect(screen.getAllByText(/R\s?15,00/).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale!.totalCents).toBe(1500);
    expect(sale!.lines[0]).toMatchObject({ priceCents: 1800, discountCents: 300, promotionId: null, priceOverrideBy: "boss-1" });
  });

  it("changes nothing on a wrong PIN, and the line keeps its price", async () => {
    const { user } = await setup();
    await scanBread(user);

    await askManager(user, "15", "1111");

    expect(await screen.findByText("That PIN is not right. Tries left: 4.")).toBeTruthy();
    expect(screen.queryByText(/Price approved by/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove price change: Bread" })).toBeNull();
  });

  it("refuses a price that is not lower, or not an amount, before asking for a PIN", async () => {
    const { user } = await setup();
    await scanBread(user);

    await askManager(user, "18", "");
    expect(screen.getByRole("status").textContent).toContain("must be lower than");
    expect(screen.queryByLabelText("Manager PIN")).toBeNull();
    await user.clear(screen.getByRole("textbox", { name: "New price for Bread" }));
    await user.type(screen.getByRole("textbox", { name: "New price for Bread" }), "abc");
    await user.click(screen.getByRole("button", { name: "Ask a manager" }));
    expect(screen.getByRole("status").textContent).toContain("Type the new price as an amount");
  });

  it("says so when there is no manager on the till to approve", async () => {
    const { user } = await setup({ managers: false });
    await scanBread(user);

    await askManager(user, "15", "");

    expect((await screen.findByRole("alert")).textContent).toContain("no manager on this till");
  });

  it("can be taken off again", async () => {
    const { user } = await setup();
    await scanBread(user);
    await askManager(user, "15", "4321");

    await user.click(await screen.findByRole("button", { name: "Remove price change: Bread" }));

    expect(screen.queryByText(/Price approved by/)).toBeNull();
    expect(screen.getByRole("button", { name: "Change price: Bread" })).toBeTruthy();
  });
});

describe("vouchers, points and the wallet in a split payment", () => {
  const flags = ["split_payments", "vouchers", "loyalty", "school_wallet", "customers"];

  async function toSplit(user: User) {
    await scanBread(user);
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Split payment" }));
  }

  it("takes the voucher through the voucher helper and pays the sale with its reference", async () => {
    const post = vi.fn(async (_path: string, _body?: unknown) => ok({ id: "v1", codeLast4: "1234", balanceCents: 500, expiresOn: null, isVoid: false, isExpired: false }));
    const { user, outbox } = await setup({ flags, api: { post } });
    await toSplit(user);

    await user.click(screen.getByRole("button", { name: "Use voucher" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD-1234");
    expect((screen.getByRole("textbox", { name: "Amount to take" }) as HTMLInputElement).value).toBe("18.00");
    await user.click(screen.getByRole("button", { name: "Take from voucher" }));

    await waitFor(() => expect(screen.getByText(/Taken and locked in: reference vr-/)).toBeTruthy());
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/vouchers/redeem`);
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: DEVICE, staffId: staff.id, code: "ABCD-1234", amountCents: 1800 });
    await user.click(screen.getByRole("button", { name: "Pay" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    const reference = (post.mock.calls[0]![1] as { reference: string }).reference;
    expect(sale).toMatchObject({ paymentMethodCode: "split", payments: [{ methodCode: "voucher", amountCents: 1800, reference }] });
    expect((post.mock.calls[0]![1] as { saleId: string }).saleId).toBe(sale!.id);
  });

  it("charges part of the bill to the wallet with the sale's own id, and the rest goes by cash", async () => {
    const post = vi.fn(async (_path: string, _body?: unknown) => ok({ purchaseId: "pur-1", holderId: "h1", holderName: "Sipho Mokoena", grade: null, className: null, hasPhoto: false, amountCents: 1000, balanceCents: 500, repeated: false }));
    const { user, outbox } = await setup({ flags, api: { post } });
    await toSplit(user);

    await user.click(screen.getByRole("button", { name: "Pay from wallet" }));
    await user.type(screen.getByRole("textbox", { name: "Wallet QR code or card number" }), "qr-token");
    await user.clear(screen.getByRole("textbox", { name: "Amount to take" }));
    await user.type(screen.getByRole("textbox", { name: "Amount to take" }), "10");
    await user.click(screen.getByRole("button", { name: "Take from wallet" }));

    expect(await screen.findByText(/Took R\s?10,00 from Sipho Mokoena's wallet/)).toBeTruthy();
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/wallet-payments`);
    expect(post.mock.calls[0]![1]).toMatchObject({ amountCents: 1000, qrToken: "qr-token", token: null, items: [{ productId: "p-bread", quantity: 1, priceCents: 1800 }] });
    await user.click(screen.getByRole("button", { name: "Add a part" }));
    await user.click(screen.getByRole("button", { name: "Pay" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale!.payments).toEqual([
      { methodCode: "wallet", amountCents: 1000, reference: "pur-1", tenderedCents: null },
      { methodCode: "cash", amountCents: 800, reference: null, tenderedCents: 800 },
    ]);
    expect((post.mock.calls[0]![1] as { saleId: string }).saleId).toBe(sale!.id);
  });

  it('uses points as their own points part with the redemption reference, after checking what they are worth', async () => {
    const get = vi.fn(async () => ok({ customerId: "c1", points: 120, valueCents: 1200, minRedeemPoints: 50, recent: [] }));
    const post = vi.fn(async (_path: string, _body?: unknown) => ok({ redemptionReference: "pr-server-1", points: 50, valueCents: 500, balanceAfter: 70 }));
    let bridge: SaleBridge | null = null;
    const { user, outbox } = await setup({ flags, api: { get, post }, onBridge: (b) => { bridge = b ?? bridge; } });
    await scanBread(user);
    act(() => bridge!.setCustomer("c1", "Anele Dlamini"));
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Split payment" }));

    await user.click(screen.getByRole("button", { name: "Use points" }));
    await user.type(screen.getByRole("textbox", { name: "Points to use" }), "50");
    await user.click(screen.getByRole("button", { name: "Take points" }));

    await waitFor(() => expect(screen.getByText("Taken and locked in: reference pr-server-1")).toBeTruthy());
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/customers/c1/points/redeem`);
    expect(post.mock.calls[0]![1]).toMatchObject({ points: 50, deviceId: DEVICE, staffId: staff.id });
    await user.click(screen.getByRole("button", { name: "Add a part" }));
    await user.click(screen.getByRole("button", { name: "Pay" }));

    await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
    const [sale] = await queuedSales(outbox);
    expect(sale).toMatchObject({ customerId: "c1", payments: [{ methodCode: "points", amountCents: 500, reference: "pr-server-1" }, { methodCode: "cash", amountCents: 1300 }] });
  });

  it("will not take points worth more than what is left, nor without a customer, and takes nothing", async () => {
    const get = vi.fn(async () => ok({ customerId: "c1", points: 120, valueCents: 12_000, minRedeemPoints: 50, recent: [] }));
    const post = vi.fn();
    let bridge: SaleBridge | null = null;
    const { user } = await setup({ flags, api: { get, post }, onBridge: (b) => { bridge = b ?? bridge; } });
    await toSplit(user);

    await user.click(screen.getByRole("button", { name: "Use points" }));
    await user.type(screen.getByRole("textbox", { name: "Points to use" }), "50");
    await user.click(screen.getByRole("button", { name: "Take points" }));
    expect(screen.getByRole("status").textContent).toContain("Attach the customer to the sale first");

    act(() => bridge!.setCustomer("c1", "Anele"));
    await user.click(screen.getByRole("button", { name: "Take points" }));
    await waitFor(() => expect(screen.getByRole("status").textContent).toMatch(/worth R.50,00/));
    expect(post).not.toHaveBeenCalled();
  });

  it("shows the server's refusal and adds no part when the voucher is refused", async () => {
    const post = vi.fn(async () => fail("voucher.insufficient", "The voucher does not have enough on it."));
    const { user } = await setup({ flags, api: { post } });
    await toSplit(user);

    await user.click(screen.getByRole("button", { name: "Use voucher" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD");
    await user.click(screen.getByRole("button", { name: "Take from voucher" }));

    expect(await screen.findByText("The voucher does not have enough on it.")).toBeTruthy();
    expect(screen.queryByText(/Taken and locked in/)).toBeNull();
  });

  it("uses the same reference when the same voucher is tried again, so a lost answer never takes the money twice", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(fail("network.unreachable", "We cannot reach the server."))
      .mockResolvedValueOnce(ok({ id: "v1", codeLast4: "1234", balanceCents: 0, expiresOn: null, isVoid: false, isExpired: false }));
    const { user } = await setup({ flags, api: { post } });
    await toSplit(user);
    await user.click(screen.getByRole("button", { name: "Use voucher" }));
    await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD");

    await user.click(screen.getByRole("button", { name: "Take from voucher" }));
    await screen.findByText("We cannot reach the server.");
    await user.click(screen.getByRole("button", { name: "Take from voucher" }));

    await waitFor(() => expect(screen.getByText(/Taken and locked in/)).toBeTruthy());
    expect((post.mock.calls[1]![1] as { reference: string }).reference).toBe((post.mock.calls[0]![1] as { reference: string }).reference);
  });

  it("is disabled offline with a plain message, and calls nothing", async () => {
    const post = vi.fn();
    const { user } = await setup({ flags, api: { post }, online: false });
    await toSplit(user);

    expect(screen.getByText("Vouchers, points and wallets need the internet. Connect the till to use them.")).toBeTruthy();
    for (const name of ["Use voucher", "Use points", "Pay from wallet"]) expect((screen.getByRole("button", { name }) as HTMLButtonElement).disabled).toBe(true);
    expect(post).not.toHaveBeenCalled();
  });

  describe("giving back what was taken when the sale does not go ahead", () => {
    const voucherOk = { id: "v1", codeLast4: "1234", balanceCents: 500, expiresOn: null, isVoid: false, isExpired: false };
    const walletOk = { purchaseId: "pur-9", holderId: "h1", holderName: "Sipho", grade: null, className: null, hasPhoto: false, amountCents: 1000, balanceCents: 0, repeated: false };
    const backTwice = async (user: User) => {
      await user.click(screen.getByRole("button", { name: "Back" }));
      await user.click(screen.getByRole("button", { name: "Back" }));
    };

    async function takeVoucher(user: User) {
      await user.click(screen.getByRole("button", { name: "Use voucher" }));
      await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD-1234");
      await user.click(screen.getByRole("button", { name: "Take from voucher" }));
      await screen.findByText(/Taken and locked in/);
    }

    it("releases the voucher when the cashier leaves the pay step, and says so", async () => {
      const post = vi.fn(async (_path: string, _body?: unknown) => ok(voucherOk));
      const { user, kv } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);

      await backTwice(user);

      expect(await screen.findByText("The voucher amount was given back.")).toBeTruthy();
      const release = post.mock.calls.find((c) => c[0] === `/shops/${SHOP}/vouchers/release`)!;
      const redeem = post.mock.calls[0]![1] as { reference: string };
      expect(release[1]).toEqual({ deviceId: DEVICE, staffId: staff.id, code: "ABCD-1234", reference: redeem.reference });
      expect(await pendingReleases(kv)).toEqual([]);
    });

    it("keeps a note of a taken part while the sale is open, so a till closed mid-payment gives it back on the next start", async () => {
      const post = vi.fn(async (_path: string, _body?: unknown) => ok(voucherOk));
      const { user, kv, outbox } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);

      const held = (await kv.get<Array<{ kind: string; code: string }>>("releases.held"))!;
      expect(held).toHaveLength(1);
      expect(held[0]).toMatchObject({ kind: "voucher", code: "ABCD-1234" });
      expect(await pendingReleases(kv)).toEqual([]);   // nothing is given back while the cashier is still paying

      // The till is closed here. When it starts again, the held note becomes a note to give back, and is sent.
      expect(await adoptHeldReleases(kv)).toBe(1);
      expect(await kv.get("releases.held")).toEqual([]);
      const caller = { shopId: SHOP, deviceId: DEVICE, staffId: staff.id };
      const r = await sendPendingReleases({ kv, api: { post } as unknown as Api, outbox, caller });
      expect(r.given).toBe(1);
      expect(post.mock.calls.some((c) => c[0] === `/shops/${SHOP}/vouchers/release`)).toBe(true);
      expect(await pendingReleases(kv)).toEqual([]);
    });

    it("drops the held note when the sale goes through, and when the part is given back", async () => {
      const post = vi.fn(async (_path: string, _body?: unknown) => ok(voucherOk));
      const { user, kv, outbox } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);
      await user.click(screen.getByRole("button", { name: "Pay" }));
      await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
      expect(await kv.get("releases.held")).toEqual([]);
      cleanup();

      const second = await setup({ flags, api: { post } });
      await toSplit(second.user);
      await takeVoucher(second.user);
      await backTwice(second.user);
      await screen.findByText("The voucher amount was given back.");
      expect(await second.kv.get("releases.held")).toEqual([]);
    });

    it("releases the voucher, the wallet payment and the points (with their reference) together, so a voucher and points share a sale", async () => {
      const get = vi.fn(async () => ok({ customerId: "c1", points: 120, valueCents: 1200, minRedeemPoints: 50, recent: [] }));
      const post = vi.fn(async (path: string, _body?: unknown) => ok(path.endsWith("/redeem") && path.includes("customers") ? { redemptionReference: "pr-9", points: 50, valueCents: 500, balanceAfter: 70 } : path.includes("wallet") ? walletOk : voucherOk));
      let bridge: SaleBridge | null = null;
      const { user } = await setup({ flags, api: { get, post }, onBridge: (b) => { bridge = b ?? bridge; } });
      await scanBread(user);
      act(() => bridge!.setCustomer("c1", "Anele"));
      await user.click(screen.getByRole("button", { name: /^Pay/ }));
      await user.click(screen.getByRole("button", { name: "Split payment" }));
      await user.click(screen.getByRole("button", { name: "Use points" }));
      await user.type(screen.getByRole("textbox", { name: "Points to use" }), "50");
      await user.click(screen.getByRole("button", { name: "Take points" }));
      await screen.findByText("Taken and locked in: reference pr-9");
      await user.click(screen.getByRole("button", { name: "Pay from wallet" }));
      await user.type(screen.getByRole("textbox", { name: "Wallet QR code or card number" }), "qr");
      await user.clear(screen.getByRole("textbox", { name: "Amount to take" }));
      await user.type(screen.getByRole("textbox", { name: "Amount to take" }), "10");
      await user.click(screen.getByRole("button", { name: "Take from wallet" }));
      await screen.findByText(/Took R\s?10,00/);
      await user.click(screen.getByRole("button", { name: "Use voucher" }));
      await user.type(screen.getByRole("textbox", { name: "Voucher code" }), "ABCD");
      await user.clear(screen.getByRole("textbox", { name: "Amount to take" }));
      await user.type(screen.getByRole("textbox", { name: "Amount to take" }), "3");
      await user.click(screen.getByRole("button", { name: "Take from voucher" }));
      await waitFor(() => expect(screen.getAllByText(/Taken and locked in/).length).toBe(3));

      await backTwice(user);

      await waitFor(() => expect(post.mock.calls.filter((c) => c[0].includes("release") || c[0].includes("void")).length).toBe(3));
      expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/customers/c1/points/release`, { deviceId: DEVICE, staffId: staff.id, reference: "pr-9" });
      expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/wallet-payments/pur-9/void`, { deviceId: DEVICE, staffId: staff.id });
      expect(post.mock.calls.some((c) => c[0] === `/shops/${SHOP}/vouchers/release`)).toBe(true);
    });

    it("keeps a note when the till is offline, and gives it back once it is online", async () => {
      const post = vi.fn(async (_path: string, _body?: unknown) => ok(voucherOk));
      const { user, kv, outbox, rerenderOnline } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);
      rerenderOnline(false);

      await backTwice(user);

      expect(await screen.findByText(/will be given back as soon as the till is online/)).toBeTruthy();
      expect(post.mock.calls.some((c) => c[0].endsWith("/release"))).toBe(false);
      expect(await pendingReleases(kv)).toHaveLength(1);

      const caller = { shopId: SHOP, deviceId: DEVICE, staffId: staff.id };
      const r = await sendPendingReleases({ kv, api: { post } as unknown as Api, outbox, caller });
      expect(r).toEqual({ given: 1, waiting: 0, failed: [] });
      expect(await pendingReleases(kv)).toEqual([]);
    });

    it("keeps the note when the call is lost, and tells the cashier plainly when the server refuses", async () => {
      let answer: () => unknown = () => fail("network.unreachable", "No connection.");
      const post = vi.fn(async (path: string, _body?: unknown) => (path.endsWith("/release") ? (answer() as never) : ok(voucherOk)));
      const { user, kv } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);
      await backTwice(user);
      await screen.findByText(/will be given back as soon as the till is online/);
      expect(await pendingReleases(kv)).toHaveLength(1);

      answer = () => fail("payment.sale_recorded", "The sale has already reached the server.");
      await user.click(screen.getByRole("button", { name: /^Pay/ }));
      await user.click(screen.getByRole("button", { name: "Split payment" }));
      await takeVoucher(user);
      await backTwice(user);
      expect(await screen.findByText(/Could not give back what was taken: The sale has already reached the server\./)).toBeTruthy();
    });

    it("never releases once the sale is queued, and drops a waiting note whose sale is in the queue", async () => {
      const post = vi.fn(async (_path: string, _body?: unknown) => ok(voucherOk));
      const { user, kv, outbox } = await setup({ flags, api: { post } });
      await toSplit(user);
      await takeVoucher(user);
      await user.click(screen.getByRole("button", { name: "Pay" }));
      await waitFor(async () => expect((await queuedSales(outbox)).length).toBe(1));
      expect(post.mock.calls.some((c) => c[0].endsWith("/release"))).toBe(false);

      const [sale] = await queuedSales(outbox);
      await kv.set("releases.pending", [{ kind: "voucher", code: "X", reference: "r", saleId: sale!.id }]);
      const caller = { shopId: SHOP, deviceId: DEVICE, staffId: staff.id };
      post.mockClear();
      expect(await sendPendingReleases({ kv, api: { post } as unknown as Api, outbox, caller })).toEqual({ given: 0, waiting: 0, failed: [] });
      expect(post).not.toHaveBeenCalled();
      expect(await pendingReleases(kv)).toEqual([]);
    });
  });

  it("offers each only when the pack has it on", async () => {
    const { user } = await setup({ flags: ["split_payments", "vouchers"], api: {} });
    await toSplit(user);

    expect(screen.getByRole("button", { name: "Use voucher" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Use points" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Pay from wallet" })).toBeNull();
  });
});

describe("serial numbers after a sale", () => {
  async function sellPhone(user: User) {
    await user.type(entry(), "6001000000099{Enter}");
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));
  }

  it("offers the serial fields for an item that needs one and keeps them for the sale until it has been sent", async () => {
    const onSerialsQueued = vi.fn();
    const { user, kv, outbox } = await setup({ onSerialsQueued });
    await sellPhone(user);

    await user.type(await screen.findByRole("textbox", { name: "Serial number 1 for Phone" }), " SN-0001 ");
    await user.click(screen.getByRole("button", { name: "Save serial numbers" }));

    await waitFor(async () => expect(await pendingSerials(kv)).toHaveLength(1));
    const [sale] = await queuedSales(outbox);
    expect(await pendingSerials(kv)).toEqual([{ saleId: sale!.id, items: [{ productId: "p-phone", serial: "SN-0001" }] }]);
    expect(onSerialsQueued).toHaveBeenCalled();
    expect(screen.getByText(/Serial numbers saved/)).toBeTruthy();
    expect(screen.queryByRole("textbox", { name: "Serial number 1 for Phone" })).toBeNull();
  });

  it("asks for one field for each unit sold, and wants at least one number", async () => {
    const { user, kv } = await setup();
    await user.type(entry(), "6001000000099{Enter}");
    await user.type(entry(), "6001000000099{Enter}");
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));

    expect(await screen.findByRole("textbox", { name: "Serial number 2 for Phone" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Save serial numbers" }));
    expect(screen.getByText("Type at least one serial number.")).toBeTruthy();
    expect(await pendingSerials(kv)).toEqual([]);
  });

  it("does not ask for anything on an item with no serial number, and can be skipped", async () => {
    const { user } = await setup();
    await scanBread(user);
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));
    await screen.findByText(/Sale saved/i).catch(() => undefined);
    expect(screen.queryByRole("button", { name: "Save serial numbers" })).toBeNull();

    await sellPhone(user);
    await user.click(await screen.findByRole("button", { name: "Not now" }));
    expect(screen.queryByRole("button", { name: "Save serial numbers" })).toBeNull();
  });
});
