import type { Api } from "@tillknowit/api-client";
import { Button } from "@tillknowit/design-system";
import {
  MAX_PAYMENT_PARTS, addItem, addWeighed, creditSaleRecord, emptyBasket, fail, formatRand, newId, ok, parseRandToCents, queueBuilt, queueSale, quickCashOptions, clearPriceOverride, removeItem, scan, setPriceOverride, setQuantity, setTip,
  summariseBasket, toSaleUpload, wouldExceedLimit,
  type Basket, type BasketEntry, type CatalogueItem, type EmbeddedBarcodeRules, type Outbox, type Payment, type PromotionDef, type Result,
} from "@tillknowit/domain";
import { PAPER_COLUMNS, buildSlip, readStableWeight, type SlipData, type WeighOptions } from "@tillknowit/hardware";
import { translate, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import type { Features } from "@tillknowit/packs";
import { useEffect, useMemo, useRef, useState } from "react";
import { ManagerApproval } from "./coreKit";
import type { CreditAccount } from "./creditAccounts";
import type { SaleBridge, TillHardware } from "./feature";
import { getPoints, newPointsReference, redeemPoints } from "./features/customers/customerApi";
import type { Caller } from "./features/till-kit/kit";
import { newRedemptionReference, redeemVoucher } from "./features/vouchers/voucherApi";
import { takeWalletPayment, type WalletItem } from "./features/wallet-pay/walletApi";
import type { KeyValueStore } from "./localData";
import { queuePendingSerials } from "./pendingSerials";
import styles from "./SellScreen.module.css";
import { loadStaff, type StaffRecord } from "./staffStore";
import type { DeviceConfig } from "./syncRunner";
import { hardwareText, tm, type TillKey } from "./tillMessages";

interface Props {
  staff: StaffRecord;
  language: LanguageCode;
  catalogue: readonly CatalogueItem[];
  promotions: readonly PromotionDef[];
  /** Scale label rules, when the shop's pack uses them. */
  barcodeRules: EmbeddedBarcodeRules | null;
  outbox: Outbox;
  /** Called after a sale is safely in the queue, so the till can try to send it. */
  onSaleQueued?: () => void;
  /** Makes the time and the ids; tests pass their own. */
  clock?: () => Date;
  /** The shop's business pack. Tips, split payments, customers, the printer and the cash drawer each need their feature on. */
  features?: Features;
  hardware?: TillHardware;
  api?: Api;
  device?: DeviceConfig | null;
  online?: boolean;
  /** The credit customers this till knows. */
  creditAccounts?: readonly CreditAccount[];
  shopName?: string;
  /** The till's small database: where the managers who can approve a price come from, and where serial numbers wait for their sale. */
  kv?: KeyValueStore;
  /** Called after serial numbers were saved for a sale, so the till can send them as soon as the sale has arrived. */
  onSerialsQueued?: () => void;
  /** Lets other tabs put things in this sale. Called with the bridge when this screen is showing, and with null when it goes away. */
  onBridge?: (bridge: SaleBridge | null) => void;
  weighOptions?: WeighOptions;
}

type Step = "basket" | "pay" | "cash" | "split" | "credit";
type SplitMethod = "cash" | "card" | "qr" | "wallet" | "voucher";
const splitMethods: readonly SplitMethod[] = ["cash", "card", "qr", "wallet", "voucher"];

interface SplitPart {
  method: SplitMethod;
  amount: string;
  reference: string;
  received: string;
  /** Taken from a voucher, points or a wallet already: the money has left, so the amount and the reference can no longer change. */
  locked?: boolean;
}

type OnlineForm = "voucher" | "points" | "wallet";

interface Receipt {
  saleId: string;
  slip: SlipData;
  cash: boolean;
  url: string | null;
  note: string;
  /** Items just sold that need serial numbers, with how many. Empty once they were saved or skipped. */
  serials: Array<{ productId: string; name: string; count: number }>;
}

const MAX_GRAMS_TEXT = /^\d+$/;
/** The most serial fields shown for one product. The server takes up to 200 numbers at a time. */
const MAX_SERIAL_FIELDS = 50;

/** A line at an agreed price (a ticket total, a service). It is never discounted and the same product adds into one line, like a price label from a scale. */
function withFixedLine(basket: Basket, line: { productId: string; name: string; priceCents: number; costCents?: number; quantity?: number }): Result<Basket> {
  const quantity = line.quantity ?? 1;
  if (!Number.isInteger(quantity) || quantity < 1) return fail("basket.quantity_invalid", "Quantity must be a whole number of one or more.");
  if (!Number.isInteger(line.priceCents) || line.priceCents < 0) return fail("sale.line_price", "Prices cannot be negative.");
  const existing = basket.entries.find((e) => e.productId === line.productId);
  if (existing && existing.fixedTotalCents === undefined) return fail("basket.label_mixed", `${line.name} is already in the sale. Remove it to add it at this price.`);
  const price = line.priceCents * quantity + (existing?.fixedTotalCents ?? 0);
  const entry: BasketEntry = { productId: line.productId, name: line.name, quantity: 1, unitPriceCents: price, costCents: (line.costCents ?? 0) * quantity + (existing?.costCents ?? 0), unitDivisor: 1, fixedTotalCents: price };
  return ok({ ...basket, entries: existing ? basket.entries.map((e) => (e.productId === line.productId ? entry : e)) : [...basket.entries, entry] });
}

/**
 * The selling screen. A sale is built in the basket, paid, and written to the outbox before the screen says it is done: nothing here waits for a network.
 * All the rules (prices, promotions, VAT, tips, split payments) are in @tillknowit/domain, the same as the server's. The slip and the cash drawer are a convenience
 * after the sale is saved: if they fail the sale stands and the cashier is told in plain words.
 */
export function SellScreen({
  staff, language, catalogue, promotions, barcodeRules, outbox, onSaleQueued, clock = () => new Date(), features, hardware, api, device, online = true, creditAccounts = [], shopName, kv, onSerialsQueued, onBridge, weighOptions,
}: Props) {
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);
  const x = (key: TillKey, params?: Record<string, string | number>) => tm(language, key, params);
  const [basket, setBasketState] = useState<Basket>(emptyBasket);
  const basketRef = useRef<Basket>(emptyBasket);
  const [step, setStep] = useState<Step>("basket");
  const [entry, setEntry] = useState("");
  const [weighing, setWeighing] = useState<CatalogueItem | null>(null);
  const [grams, setGrams] = useState("");
  const [cashGiven, setCashGiven] = useState("");
  const [message, setMessage] = useState("");
  const [saved, setSaved] = useState("");
  const [tipText, setTipText] = useState("");
  const [parts, setParts] = useState<SplitPart[]>([]);
  const [phone, setPhone] = useState("");
  const [customerName, setCustomerName] = useState("");
  const [found, setFound] = useState<Array<{ id: string; name: string; phone: string }>>([]);
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  // A manager's price override: which line is being changed, what was typed, and the names of the managers who approved (by staff id).
  const [priceFor, setPriceFor] = useState<{ productId: string; text: string; cents: number | null } | null>(null);
  const [approvers, setApprovers] = useState<Record<string, string>>({});
  // Vouchers, points and the school wallet, taken from the split payment panel.
  const [onlineForm, setOnlineForm] = useState<OnlineForm | null>(null);
  const [onlineAmount, setOnlineAmount] = useState("");
  const [voucherCode, setVoucherCode] = useState("");
  const [pointsText, setPointsText] = useState("");
  const [walletToken, setWalletToken] = useState("");
  const [walletKind, setWalletKind] = useState<"qr" | "card">("qr");
  const [onlineBusy, setOnlineBusy] = useState(false);
  const [serialText, setSerialText] = useState<Record<string, string>>({});
  // The id of the sale being rung up. It is made once, because a wallet payment is tied to it, and it becomes the sale's id when the sale is saved.
  const saleIdRef = useRef<string | null>(null);
  // References for online redemptions, kept per attempt so pressing again after a lost answer is the same redemption and never takes the money twice.
  const attemptRefs = useRef(new Map<string, string>());

  const setBasket = (next: Basket) => {
    basketRef.current = next;
    setBasketState(next);
  };

  const summary = useMemo(() => summariseBasket(basket, promotions, clock()), [basket, promotions, clock]);
  const tipsOn = features?.isOn("tips") ?? false;
  const splitOn = features?.isOn("split_payments") ?? false;
  const customersOn = (features?.isOn("customers") ?? false) && api !== undefined && device != null;
  const printerOn = (features?.isOn("printer") ?? false) && hardware?.printer !== undefined;

  const matches = useMemo(() => {
    const q = entry.trim().toLowerCase();
    if (q.length < 2 || /^\d+$/.test(q)) return [];
    return catalogue.filter((c) => c.name.toLowerCase().includes(q)).slice(0, 6);
  }, [entry, catalogue]);

  // Other tabs put things in the sale through this. It works on the latest basket, not the one this render saw.
  useEffect(() => {
    if (!onBridge) return;
    onBridge({
      addLine: (line) => {
        const next = withFixedLine(basketRef.current, line);
        if (!next.ok) return next;
        setBasket(next.value);
        setReceipt(null);
        return ok(undefined);
      },
      addProduct: (productId, quantity = 1) => {
        const item = catalogue.find((c) => c.id === productId);
        if (!item) return fail("basket.unknown_item", "That product is not on this till.");
        const next = item.soldByWeight ? addWeighed(basketRef.current, item, quantity) : addItem(basketRef.current, item, quantity);
        if (!next.ok) return next;
        setBasket(next.value);
        setReceipt(null);
        return ok(undefined);
      },
      setCustomer: (customerId, name) => {
        setBasket({ ...basketRef.current, customerId });
        setCustomerName(customerId ? name ?? "" : "");
      },
    });
    return () => onBridge(null);
  }, [onBridge, catalogue]);

  function problem(code: string): string {
    return code === "basket.unknown_item" ? t("sell.unknownCode") : code.startsWith("weight.") ? t("sell.error.weight") : code === "sale.tip_invalid" || code === "basket.tip_invalid" ? x("sell.tipTooBig") : t("sell.error.generic");
  }

  function fresh() {
    setSaved("");
    setMessage("");
    setReceipt(null);
  }

  function pick(item: CatalogueItem) {
    fresh();
    setEntry("");
    if (item.soldByWeight) return setWeighing(item);
    const added = addItem(basket, item);
    if (added.ok) setBasket(added.value);
    else setMessage(problem(added.code));
  }

  function submitEntry() {
    const code = entry.trim();
    if (code === "") return;
    fresh();
    const outcome = scan(basket, code, catalogue, barcodeRules);
    if (outcome.ok) {
      setEntry("");
      if (outcome.value.kind === "weigh") setWeighing(outcome.value.item);
      else setBasket(outcome.value.basket);
    } else if (matches.length === 1 && matches[0]) pick(matches[0]);
    else if (matches.length === 0) setMessage(/^\d+$/.test(code) ? problem(outcome.code) : t("sell.noMatch"));
  }

  function submitWeight() {
    if (!weighing) return;
    const added = addWeighed(basket, weighing, Number(grams));
    if (added.ok) {
      setBasket(added.value);
      setWeighing(null);
      setGrams("");
      setMessage("");
    } else setMessage(problem(added.code));
  }

  async function readScale() {
    if (!hardware?.scale) return;
    try {
      setGrams(String(await readStableWeight(hardware.scale, weighOptions)));
      setMessage("");
    } catch (error) {
      setMessage(hardwareText(language, error));
    }
  }

  // ---- tips ----

  function applyTip(cents: number) {
    const next = setTip(basket, cents);
    if (!next.ok) return setMessage(problem(next.code));
    if (cents > summary.totalCents) return setMessage(x("sell.tipTooBig"));
    setMessage("");
    setBasket(next.value);
  }

  function submitTip() {
    const cents = tipText.trim() === "" ? 0 : parseRandToCents(tipText);
    if (cents === null) return setMessage(x("sell.tipInvalid"));
    applyTip(cents);
    if (cents <= summary.totalCents) setTipText("");
  }

  // ---- customers ----

  async function findCustomer() {
    if (!api || !device) return;
    if (!online) return setMessage(x("sell.customerOnline"));
    const result = await api.get<Array<{ id: string; name: string; phone: string }>>(`/shops/${device.shopId}/customers`, { search: phone.trim(), deviceId: device.deviceId, staffId: staff.id });
    if (!result.ok) return setMessage(result.message);
    if (result.value.length === 0) return setMessage(x("sell.customerNone"));
    setMessage("");
    if (result.value.length === 1 && result.value[0]) attach(result.value[0]);
    else setFound(result.value);
  }

  function attach(customer: { id: string; name: string }) {
    setBasket({ ...basketRef.current, customerId: customer.id });
    setCustomerName(customer.name);
    setFound([]);
    setPhone("");
  }

  function detach() {
    setBasket({ ...basketRef.current, customerId: null });
    setCustomerName("");
  }

  // ---- finishing the sale ----

  async function printAndOpen(slip: SlipData, cash: boolean): Promise<string> {
    const problems: string[] = [];
    const drawerOn = features?.isOn("cash_drawer") ?? false;
    const separateDrawer = hardware?.drawer;
    if (printerOn && hardware?.printer) {
      try {
        // With no separate drawer the printer opens the drawer, as part of the same job.
        await hardware.printer.print(buildSlip(slip, PAPER_COLUMNS.mm80, { openDrawer: drawerOn && cash && !separateDrawer }));
      } catch (error) {
        problems.push(x("sell.printFailed", { reason: hardwareText(language, error) }));
      }
    }
    if (drawerOn && cash && separateDrawer) {
      try {
        await separateDrawer.open();
      } catch (error) {
        problems.push(x("sell.drawerFailed", { reason: hardwareText(language, error) }));
      }
    }
    return problems.join(" ");
  }

  async function finish(payments: Payment[]) {
    const now = clock();
    const saleId = saleIdRef.current ?? newId(now.getTime());
    const sale = toSaleUpload(basket, summary, payments, { saleId, staffId: staff.id }, now);
    if (!sale.ok) return setMessage(problem(sale.code));
    await queueSale(outbox, sale.value, now);
    saleIdRef.current = null;
    attemptRefs.current.clear();
    // Items that take serial numbers are offered right away; the numbers wait on the till until the sale has reached the server.
    const serials = kv ? summary.lines.filter((l) => l.unitDivisor === 1 && catalogue.find((c) => c.id === l.productId)?.requiresSerial).map((l) => ({ productId: l.productId, name: l.name, count: Math.min(l.quantity, MAX_SERIAL_FIELDS) })) : [];

    // A credit sale also needs its entry in the customer's account; the server does not make it. It is queued just after the sale so the sale arrives first.
    const credit = payments.length === 1 && payments[0]?.method === "credit" ? payments[0] : null;
    if (credit?.accountId) {
      const later = new Date(now.getTime() + 1);
      await queueBuilt(outbox, "credit_entry", creditSaleRecord({ accountId: credit.accountId, saleId, totalCents: sale.value.totalCents }, { id: newId(later.getTime()), staffId: staff.id, at: later }), later);
    }

    const change = sale.value.changeCents;
    const base = t("sell.saved", { amount: formatRand(summary.dueCents) }) + (change ? ` ${t("sell.change", { amount: formatRand(change) })}` : "");
    const cash = payments.some((p) => p.method === "cash");
    const slip: SlipData = {
      shopName: shopName ?? t("app.name"),
      lines: summary.lines.map((l) => (l.unitDivisor === 1000 ? { name: `${l.name} (${l.quantity} g)`, quantity: 1, totalCents: l.totalCents } : { name: l.name, quantity: l.quantity, totalCents: l.totalCents })),
      totalCents: summary.dueCents, vatCents: summary.vatCents,
      paymentMethod: payments.length > 1 ? "Split" : (payments[0]?.method ?? "").replace(/^./, (c) => c.toUpperCase()), soldAt: now,
    };
    setBasket(emptyBasket);
    setStep("basket");
    setCashGiven("");
    setTipText("");
    setCustomerName("");
    setParts([]);
    setPriceFor(null);
    setOnlineForm(null);
    setSerialText({});
    setMessage("");
    setSaved(base);
    setReceipt({ saleId, slip, cash, url: null, note: "", serials });
    onSaleQueued?.();

    const problems = await printAndOpen(slip, cash);
    if (problems) setSaved(`${base} ${problems}`);
  }

  async function getQrSlip() {
    if (!receipt) return;
    if (!api || !device) return setReceipt({ ...receipt, note: x("sell.qrNoDevice") });
    // The server can only make a slip for a sale it has. While the sale is still in the queue it has not.
    if ((await outbox.all()).some((e) => e.id === receipt.saleId)) return setReceipt({ ...receipt, note: x("sell.qrWait") });
    if (!online) return setReceipt({ ...receipt, note: x("sell.qrOffline") });
    const result = await api.post<{ status: string; url: string; note?: string | null }>(`/sales/${receipt.saleId}/slip`, {
      shopId: device.shopId, deviceId: device.deviceId, staffId: staff.id, channel: "qr", destination: null, language,
    });
    if (!result.ok) return setReceipt({ ...receipt, note: result.message });
    setReceipt({ ...receipt, url: result.value.url, note: x("sell.qrReady", { url: result.value.url }) });
  }

  async function printWithQr() {
    if (!receipt?.url || !hardware?.printer) return;
    try {
      await hardware.printer.print(buildSlip({ ...receipt.slip, slipUrl: receipt.url }, PAPER_COLUMNS.mm80));
    } catch (error) {
      setReceipt({ ...receipt, note: x("sell.printFailed", { reason: hardwareText(language, error) }) });
    }
  }

  function startPay() {
    setSaved("");
    if (summary.tipCents > summary.totalCents) return setMessage(x("sell.tipTooBig"));
    setReceipt(null);
    setStep("pay");
  }

  // ---- split payments ----

  const rand = (cents: number) => formatRand(cents);
  const given = Math.round(Number(cashGiven.replace(",", ".")) * 100);
  const cashReady = Number.isFinite(given) && given >= summary.dueCents;

  const splitPaid = parts.reduce((sum, p) => sum + (parseRandToCents(p.amount) ?? 0), 0);
  const splitLeft = summary.dueCents - splitPaid;
  const splitPayments = (): Payment[] | null => {
    const result: Payment[] = [];
    for (const p of parts) {
      const amountCents = parseRandToCents(p.amount);
      if (amountCents === null || amountCents <= 0) return null;
      if (p.method === "cash") {
        const received = p.received.trim() === "" ? amountCents : parseRandToCents(p.received);
        if (received === null || received < amountCents) return null;
        result.push({ method: "cash", amountCents, tenderedCents: received });
      } else if (p.method === "wallet" || p.method === "voucher") {
        if (p.reference.trim() === "") return null;
        result.push({ method: p.method, amountCents, reference: p.reference.trim() });
      } else result.push({ method: p.method, amountCents });
    }
    return result;
  };
  const splitReady = parts.length > 0 && splitLeft === 0 && splitPayments() !== null;

  function startSplit() {
    setMessage("");
    setParts([{ method: "cash", amount: "", reference: "", received: "" }]);
    setStep("split");
  }

  const unusedMethod = (): SplitMethod | undefined => splitMethods.find((m) => !parts.some((p) => p.method === m));

  function addPart() {
    const method = unusedMethod();
    if (!method || parts.length >= MAX_PAYMENT_PARTS) return;
    setParts([...parts, { method, amount: splitLeft > 0 ? (splitLeft / 100).toFixed(2) : "", reference: "", received: "" }]);
  }

  const changePart = (at: number, change: Partial<SplitPart>) => setParts(parts.map((p, i) => (i === at ? { ...p, ...change } : p)));

  // ---- a manager's price override ----

  const caller: Caller | null = device ? { shopId: device.shopId, deviceId: device.deviceId, staffId: staff.id } : null;

  function askForPrice() {
    if (!priceFor) return;
    const entry = basket.entries.find((e) => e.productId === priceFor.productId);
    const cents = parseRandToCents(priceFor.text);
    if (!entry || cents === null) return setMessage(x("sell.priceInvalid"));
    if (cents >= entry.unitPriceCents) return setMessage(x("sell.priceNotLower", { price: rand(entry.unitPriceCents) }));
    setMessage("");
    setPriceFor({ ...priceFor, cents });
  }

  /** The manager's PIN was right (ManagerApproval checked it on the till): the line takes the lower price and carries who approved it. */
  async function approvePrice(managerId: string) {
    if (!priceFor || priceFor.cents === null) return;
    const next = setPriceOverride(basketRef.current, priceFor.productId, priceFor.cents, managerId);
    setPriceFor(null);
    if (!next.ok) return setMessage(next.code === "basket.label_line" ? x("sell.priceNoLabel") : problem(next.code));
    const manager = (await loadStaff(kv)).find((m) => m.id === managerId);
    setApprovers((a) => ({ ...a, [managerId]: manager?.name ?? "" }));
    setBasket(next.value);
    setMessage("");
  }

  // ---- vouchers, points and the school wallet (online only) ----

  const onlineReady = online && api !== undefined && caller !== null;
  const voucherOn = features?.isOn("vouchers") ?? false;
  const pointsOn = features?.isOn("loyalty") ?? false;
  const walletOn = features?.isOn("school_wallet") ?? false;
  const currentSaleId = () => (saleIdRef.current ??= newId(clock().getTime()));

  /** The reference of one attempt: the same while the same thing is tried again, so a lost answer never means money taken twice. */
  function attemptRef(key: string, make: () => string): string {
    const found = attemptRefs.current.get(key);
    if (found) return found;
    const made = make();
    attemptRefs.current.set(key, made);
    return made;
  }

  const isBlank = (p: SplitPart) => !p.locked && p.amount.trim() === "" && p.reference.trim() === "" && p.received.trim() === "";

  /** Why a voucher or wallet part cannot be added now, or null. A blank part and an unfinished one of the same method make room. */
  function partBlock(method: "voucher" | "wallet"): string | null {
    if (parts.some((p) => p.locked && p.method === method)) return method === "voucher" ? x("sell.voucherOnce") : x("sell.walletOnce");
    const kept = parts.filter((p) => p.locked || !(isBlank(p) || p.method === method));
    return kept.length >= MAX_PAYMENT_PARTS ? x("sell.splitMax", { count: MAX_PAYMENT_PARTS }) : null;
  }

  function takePart(method: "voucher" | "wallet", cents: number, reference: string) {
    const part: SplitPart = { method, amount: (cents / 100).toFixed(2), reference, received: "", locked: true };
    setParts((current) => [...current.filter((p) => p.locked || !(isBlank(p) || p.method === method)), part]);
    setOnlineForm(null);
    setOnlineAmount("");
  }

  function openOnline(form: OnlineForm) {
    setMessage("");
    setOnlineForm(onlineForm === form ? null : form);
    setOnlineAmount(splitLeft > 0 ? (splitLeft / 100).toFixed(2) : "");
  }

  const wantedAmount = (): number | null => {
    const cents = parseRandToCents(onlineAmount);
    return cents !== null && cents > 0 && cents <= splitLeft ? cents : null;
  };

  async function takeVoucher() {
    if (!api || !caller || onlineBusy) return;
    const blocked = partBlock("voucher");
    if (blocked) return setMessage(blocked);
    const code = voucherCode.trim();
    if (code === "") return setMessage(x("sell.voucherCodeNeeded"));
    const cents = wantedAmount();
    if (cents === null) return setMessage(x("sell.onlineAmountInvalid"));
    setOnlineBusy(true);
    const key = `voucher|${code}|${cents}`;
    const r = await redeemVoucher(api, caller, { code, amountCents: cents, reference: attemptRef(key, newRedemptionReference), saleId: currentSaleId() });
    setOnlineBusy(false);
    if (!r.ok) return setMessage(r.message);
    attemptRefs.current.delete(key);
    setVoucherCode("");
    setMessage("");
    takePart("voucher", cents, r.value.reference);
  }

  async function takePoints() {
    if (!api || !caller || onlineBusy) return;
    if (!basket.customerId) return setMessage(x("sell.pointsNeedCustomer"));
    const blocked = partBlock("voucher");
    if (blocked) return setMessage(blocked);
    const n = Number(pointsText);
    if (!Number.isInteger(n) || n <= 0) return setMessage(x("sell.pointsInvalid"));
    setOnlineBusy(true);
    // Points cannot be put back, so check what they are worth before taking them: a redemption bigger than what is left would leave the sale unable to balance.
    const info = await getPoints(api, caller, basket.customerId);
    if (!info.ok) {
      setOnlineBusy(false);
      return setMessage(info.message);
    }
    if (n > info.value.points) {
      setOnlineBusy(false);
      return setMessage(x("sell.pointsTooMany", { points: info.value.points }));
    }
    const worth = info.value.points > 0 ? Math.floor((info.value.valueCents * n) / info.value.points) : 0;
    if (worth > splitLeft) {
      setOnlineBusy(false);
      return setMessage(x("sell.pointsTooMuch", { value: rand(worth), left: rand(Math.max(splitLeft, 0)) }));
    }
    const key = `points|${basket.customerId}|${n}`;
    const r = await redeemPoints(api, caller, basket.customerId, n, attemptRef(key, newPointsReference));
    setOnlineBusy(false);
    if (!r.ok) return setMessage(r.message);
    attemptRefs.current.delete(key);
    setPointsText("");
    setMessage("");
    // Points are paid as a "voucher" part with the redemption's reference (see toSaleUpload).
    takePart("voucher", r.value.valueCents, r.value.redemptionReference);
  }

  /** The sale's lines as the wallet wants them, to check what the child may buy. Weighed and labelled lines go in as one item at their price. */
  const walletItems = (): WalletItem[] =>
    summary.lines.map((l) => (l.unitDivisor === 1 && l.fixedTotalCents === undefined
      ? { productId: l.productId, categoryCode: null, name: l.name, quantity: l.quantity, priceCents: l.unitPriceCents }
      : { productId: l.productId, categoryCode: null, name: l.name, quantity: 1, priceCents: l.totalCents }));

  async function takeWallet() {
    if (!api || !caller || onlineBusy) return;
    const blocked = partBlock("wallet");
    if (blocked) return setMessage(blocked);
    if (walletToken.trim() === "") return setMessage(x("sell.walletTokenNeeded"));
    const cents = wantedAmount();
    if (cents === null) return setMessage(x("sell.onlineAmountInvalid"));
    setOnlineBusy(true);
    // The sale's own id is the wallet's request id: asking again for the same sale never charges the wallet twice.
    const r = await takeWalletPayment(api, caller, { saleId: currentSaleId(), amountCents: cents, items: walletItems(), token: walletToken, kind: walletKind });
    setOnlineBusy(false);
    if (!r.ok) return setMessage(r.message);
    setWalletToken("");
    takePart("wallet", r.value.amountCents, r.value.purchaseId);
    setMessage(x("sell.walletTaken", { amount: rand(r.value.amountCents), name: r.value.holderName }));
  }

  // ---- serial numbers ----

  async function saveSerials() {
    if (!receipt || !kv) return;
    const items = receipt.serials
      .flatMap((s) => Array.from({ length: s.count }, (_, i) => ({ productId: s.productId, serial: (serialText[`${s.productId}#${i}`] ?? "").trim() })))
      .filter((i) => i.serial !== "");
    if (items.length === 0) return setReceipt({ ...receipt, note: x("sell.serialsNone") });
    await queuePendingSerials(kv, receipt.saleId, items);
    setSerialText({});
    setReceipt({ ...receipt, serials: [], note: x("sell.serialsSaved") });
    onSerialsQueued?.();
  }

  // ---- screens ----

  if (weighing) {
    return (
      <section className={styles.panel} aria-labelledby="weigh">
        <h1 id="weigh" className={styles.title}>{t("sell.weigh", { name: weighing.name })}</h1>
        <input className={styles.input} inputMode="numeric" aria-label={t("sell.weigh", { name: weighing.name })} value={grams} onChange={(e) => setGrams(e.target.value)} autoFocus />
        {hardware?.scale && <Button variant="secondary" onClick={() => void readScale()}>{x("sell.scaleRead")}</Button>}
        <p className={styles.message} role="status">{message}</p>
        <div className={styles.row}>
          <Button variant="quiet" onClick={() => { setWeighing(null); setGrams(""); }}>{t("sell.back")}</Button>
          <Button onClick={submitWeight} disabled={grams.trim() === "" || !MAX_GRAMS_TEXT.test(grams.trim())}>{t("sell.weightAdd")}</Button>
        </div>
      </section>
    );
  }

  if (step === "credit") {
    return (
      <section className={styles.panel} aria-labelledby="credit">
        <h1 id="credit" className={styles.title}>{x("sell.creditPick")}</h1>
        <p className={styles.due}>{t("sell.total")}: <strong>{rand(summary.dueCents)}</strong></p>
        {summary.tipCents > 0 ? <p className={styles.warning}>{x("sell.creditNoTip")}</p> : creditAccounts.length === 0 ? <p className={styles.empty}>{x("sell.creditNone")}</p> : (
          <ul className={styles.matches}>
            {creditAccounts.map((a) => (
              <li key={a.id}>
                <Button variant="secondary" className={styles.match} onClick={() => finish([{ method: "credit", amountCents: summary.dueCents, accountId: a.id }])}>
                  {a.name} <span>{x("sell.creditOwes", { amount: rand(a.balanceCents) })}</span>
                </Button>
                {wouldExceedLimit(a.balanceCents, summary.totalCents, a.creditLimitCents) && <p className={styles.warning}>{x("sell.creditOver", { name: a.name })}</p>}
              </li>
            ))}
          </ul>
        )}
        <p className={styles.message} role="status">{message}</p>
        <Button variant="quiet" onClick={() => setStep("pay")}>{t("sell.back")}</Button>
      </section>
    );
  }

  if (step === "split") {
    return (
      <section className={styles.panel} aria-labelledby="split">
        <h1 id="split" className={styles.title}>{x("sell.split")}</h1>
        <p className={styles.due}>{t("sell.total")}: <strong>{rand(summary.dueCents)}</strong></p>
        {parts.map((p, i) => (
          <div key={i} className={styles.part}>
            <label className={styles.label}>
              {x("sell.splitMethod", { n: i + 1 })}
              <select className={styles.select} value={p.method} disabled={p.locked} onChange={(e) => changePart(i, { method: e.target.value as SplitMethod })}>
                {splitMethods.filter((m) => m === p.method || !parts.some((o) => o.method === m)).map((m) => <option key={m} value={m}>{m === "cash" ? t("sell.cash") : m === "card" ? t("sell.card") : m === "qr" ? t("sell.qr") : m === "wallet" ? x("sell.wallet") : x("sell.voucher")}</option>)}
              </select>
            </label>
            <label className={styles.label}>
              {x("sell.splitAmount", { n: i + 1 })}
              <input className={styles.input} inputMode="decimal" value={p.amount} readOnly={p.locked} onChange={(e) => changePart(i, { amount: e.target.value })} />
            </label>
            {p.method === "cash" && (
              <label className={styles.label}>
                {x("sell.splitReceived", { n: i + 1 })}
                <input className={styles.input} inputMode="decimal" value={p.received} onChange={(e) => changePart(i, { received: e.target.value })} />
              </label>
            )}
            {(p.method === "wallet" || p.method === "voucher") && (
              <label className={styles.label}>
                {x("sell.splitRef", { n: i + 1 })}
                <input className={styles.input} value={p.reference} readOnly={p.locked} onChange={(e) => changePart(i, { reference: e.target.value })} />
              </label>
            )}
            {parts.length > 1 && !p.locked && <Button variant="quiet" aria-label={x("sell.splitRemove", { n: i + 1 })} onClick={() => setParts(parts.filter((_, k) => k !== i))}>✕</Button>}
            {p.locked && <span className={styles.message}>{x("sell.partTaken", { reference: p.reference })}</span>}
          </div>
        ))}
        {(voucherOn || pointsOn || walletOn) && (
          <div className={styles.section} role="group" aria-label={x("sell.onlinePay")}>
            {!online ? <p className={styles.warning}>{x("sell.onlineOffline")}</p> : !onlineReady ? <p className={styles.warning}>{x("sell.onlineNoDevice")}</p> : null}
            <div className={styles.chips}>
              {voucherOn && <Button variant="secondary" disabled={!onlineReady} aria-pressed={onlineForm === "voucher"} onClick={() => openOnline("voucher")}>{x("sell.useVoucher")}</Button>}
              {pointsOn && <Button variant="secondary" disabled={!onlineReady} aria-pressed={onlineForm === "points"} onClick={() => openOnline("points")}>{x("sell.usePoints")}</Button>}
              {walletOn && <Button variant="secondary" disabled={!onlineReady} aria-pressed={onlineForm === "wallet"} onClick={() => openOnline("wallet")}>{x("sell.useWallet")}</Button>}
            </div>
            {onlineReady && onlineForm === "voucher" && (
              <form className={styles.part} onSubmit={(e) => { e.preventDefault(); void takeVoucher(); }}>
                <label className={styles.label}>{x("sell.voucherCode")}<input className={styles.input} autoComplete="off" value={voucherCode} onChange={(e) => setVoucherCode(e.target.value)} /></label>
                <label className={styles.label}>{x("sell.onlineAmount")}<input className={styles.input} inputMode="decimal" value={onlineAmount} onChange={(e) => setOnlineAmount(e.target.value)} /></label>
                <Button type="submit" disabled={onlineBusy}>{x("sell.voucherTake")}</Button>
              </form>
            )}
            {onlineReady && onlineForm === "points" && (
              <form className={styles.part} onSubmit={(e) => { e.preventDefault(); void takePoints(); }}>
                <label className={styles.label}>{x("sell.pointsCount")}<input className={styles.input} inputMode="numeric" value={pointsText} onChange={(e) => setPointsText(e.target.value)} /></label>
                <Button type="submit" disabled={onlineBusy}>{x("sell.pointsTake")}</Button>
              </form>
            )}
            {onlineReady && onlineForm === "wallet" && (
              <form className={styles.part} onSubmit={(e) => { e.preventDefault(); void takeWallet(); }}>
                <label className={styles.label}>
                  {x("sell.walletKind")}
                  <select className={styles.select} value={walletKind} onChange={(e) => setWalletKind(e.target.value as "qr" | "card")}>
                    <option value="qr">{x("sell.walletQr")}</option>
                    <option value="card">{x("sell.walletCard")}</option>
                  </select>
                </label>
                <label className={styles.label}>{x("sell.walletToken")}<input className={styles.input} autoComplete="off" value={walletToken} onChange={(e) => setWalletToken(e.target.value)} /></label>
                <label className={styles.label}>{x("sell.onlineAmount")}<input className={styles.input} inputMode="decimal" value={onlineAmount} onChange={(e) => setOnlineAmount(e.target.value)} /></label>
                <Button type="submit" disabled={onlineBusy}>{x("sell.walletTake")}</Button>
              </form>
            )}
          </div>
        )}
        <p className={styles.due}>{splitLeft >= 0 ? x("sell.splitLeft", { amount: rand(splitLeft) }) : x("sell.splitOver", { amount: rand(-splitLeft) })}</p>
        {parts.length >= MAX_PAYMENT_PARTS && <p className={styles.message}>{x("sell.splitMax", { count: MAX_PAYMENT_PARTS })}</p>}
        <div className={styles.row}>
          <Button variant="secondary" onClick={addPart} disabled={parts.length >= MAX_PAYMENT_PARTS || unusedMethod() === undefined}>{x("sell.splitAdd")}</Button>
          <Button disabled={!splitReady} onClick={() => { const payments = splitPayments(); if (payments) void finish(payments); else setMessage(x("sell.splitInvalid")); }}>{t("sell.pay")}</Button>
        </div>
        <p className={styles.message} role="status">{message}</p>
        <Button variant="quiet" onClick={() => setStep("pay")}>{t("sell.back")}</Button>
      </section>
    );
  }

  if (step === "pay" || step === "cash") {
    return (
      <section className={styles.panel} aria-labelledby="pay">
        <h1 id="pay" className={styles.title}>{t("sell.chooseMethod")}</h1>
        <p className={styles.due}>{t("sell.total")}: <strong>{rand(summary.dueCents)}</strong></p>
        {step === "pay" ? (
          <div className={styles.methods}>
            <Button onClick={() => setStep("cash")}>{t("sell.cash")}</Button>
            <Button variant="secondary" onClick={() => finish([{ method: "card", amountCents: summary.dueCents }])}>{t("sell.card")}</Button>
            <Button variant="secondary" onClick={() => finish([{ method: "qr", amountCents: summary.dueCents }])}>{t("sell.qr")}</Button>
            <Button variant="secondary" onClick={() => setStep("credit")}>{x("sell.credit")}</Button>
            {splitOn && <Button variant="secondary" onClick={startSplit}>{x("sell.split")}</Button>}
          </div>
        ) : (
          <>
            <div className={styles.methods}>
              {quickCashOptions(summary.dueCents).map((c) => (
                <Button key={c} variant="secondary" onClick={() => finish([{ method: "cash", amountCents: summary.dueCents, tenderedCents: c }])}>
                  {c === summary.dueCents ? t("sell.exact") : rand(c)}
                </Button>
              ))}
            </div>
            <label className={styles.label}>
              {t("sell.cashGiven")}
              <input className={styles.input} inputMode="decimal" value={cashGiven} onChange={(e) => setCashGiven(e.target.value)} />
            </label>
            <Button disabled={!cashReady} onClick={() => finish([{ method: "cash", amountCents: summary.dueCents, tenderedCents: given }])}>
              {cashReady && given > summary.dueCents ? t("sell.change", { amount: rand(given - summary.dueCents) }) : t("sell.pay")}
            </Button>
          </>
        )}
        <p className={styles.message} role="status">{message}</p>
        <Button variant="quiet" onClick={() => setStep("basket")}>{t("sell.back")}</Button>
      </section>
    );
  }

  return (
    <section className={styles.panel} aria-label={t("sell.total")}>
      <form className={styles.row} onSubmit={(e) => { e.preventDefault(); submitEntry(); }}>
        <input className={styles.input} value={entry} placeholder={t("sell.scanOrSearch")} aria-label={t("sell.scanOrSearch")} onChange={(e) => setEntry(e.target.value)} autoFocus />
        <Button type="submit">{t("sell.add")}</Button>
      </form>

      {matches.length > 0 && (
        <ul className={styles.matches}>
          {matches.map((m) => <li key={m.id}><Button variant="secondary" className={styles.match} onClick={() => pick(m)}>{m.name} <span>{rand(m.priceCents)}{m.soldByWeight ? "/kg" : ""}</span></Button></li>)}
        </ul>
      )}

      <p className={styles.message} role="status" aria-live="polite">{message || saved}</p>

      {receipt && (
        <div className={styles.receipt}>
          <div className={styles.chips}>
            <Button variant="secondary" onClick={() => void getQrSlip()}>{x("sell.qrGet")}</Button>
            {receipt.url && printerOn && <Button variant="secondary" onClick={() => void printWithQr()}>{x("sell.qrPrint")}</Button>}
          </div>
          {receipt.serials.length > 0 && (
            <div className={styles.section} role="group" aria-label={x("sell.serialsOffer")}>
              <p>{x("sell.serialsOffer")}</p>
              {receipt.serials.map((s) => Array.from({ length: s.count }, (_, i) => (
                <label key={`${s.productId}#${i}`} className={styles.label}>
                  {x("sell.serialFor", { n: i + 1, name: s.name })}
                  <input className={styles.input} autoComplete="off" value={serialText[`${s.productId}#${i}`] ?? ""} onChange={(e) => setSerialText({ ...serialText, [`${s.productId}#${i}`]: e.target.value })} />
                </label>
              )))}
              <div className={styles.row}>
                <Button variant="quiet" onClick={() => { setSerialText({}); setReceipt({ ...receipt, serials: [], note: "" }); }}>{x("sell.serialsNotNow")}</Button>
                <Button onClick={() => void saveSerials()}>{x("sell.serialsSave")}</Button>
              </div>
            </div>
          )}
          {receipt.note && <p className={styles.message}>{receipt.note}</p>}
        </div>
      )}

      {summary.lines.length === 0 ? <p className={styles.empty}>{t("sell.empty")}</p> : (
        <ul className={styles.lines}>
          {summary.lines.map((l) => (
            <li key={l.productId} className={styles.line}>
              <span className={styles.name}>{l.name}{l.priceOverrideBy && <small> {x("sell.priceApprovedBy", { name: approvers[l.priceOverrideBy] || "…" })}</small>}</span>
              <span className={styles.qty}>{l.unitDivisor === 1000 ? `${l.quantity} g` : `× ${l.quantity}`}</span>
              <span className={styles.amount}>{rand(l.totalCents)}</span>
              {l.unitDivisor === 1 && l.fixedTotalCents === undefined && (
                <span className={styles.controls}>
                  <Button variant="quiet" aria-label={`${t("sell.less")}: ${l.name}`} onClick={() => { const r = setQuantity(basket, l.productId, l.quantity - 1); if (r.ok) setBasket(r.value); }}>−</Button>
                  <Button variant="quiet" aria-label={`${t("sell.more")}: ${l.name}`} onClick={() => { const r = setQuantity(basket, l.productId, l.quantity + 1); if (r.ok) setBasket(r.value); else setMessage(problem(r.code)); }}>+</Button>
                </span>
              )}
              {l.fixedTotalCents === undefined && (l.priceOverrideBy
                ? <Button variant="quiet" aria-label={`${x("sell.priceRemove")}: ${l.name}`} onClick={() => setBasket(clearPriceOverride(basket, l.productId))}>{x("sell.priceRemove")}</Button>
                : <Button variant="quiet" aria-label={`${x("sell.priceChange")}: ${l.name}`} onClick={() => { setMessage(""); setPriceFor({ productId: l.productId, text: "", cents: null }); }}>{x("sell.priceChange")}</Button>)}
              <Button variant="quiet" aria-label={`${t("sell.remove")}: ${l.name}`} onClick={() => setBasket(removeItem(basket, l.productId))}>✕</Button>
            </li>
          ))}
        </ul>
      )}

      {priceFor && (
        <div className={styles.section} role="group" aria-label={x("sell.priceNew", { name: basket.entries.find((e) => e.productId === priceFor.productId)?.name ?? "" })}>
          {priceFor.cents === null ? (
            <form className={styles.row} onSubmit={(e) => { e.preventDefault(); askForPrice(); }}>
              <input className={styles.input} inputMode="decimal" autoFocus aria-label={x("sell.priceNew", { name: basket.entries.find((e) => e.productId === priceFor.productId)?.name ?? "" })} placeholder={x("sell.priceNew", { name: basket.entries.find((e) => e.productId === priceFor.productId)?.name ?? "" })} value={priceFor.text} onChange={(e) => setPriceFor({ ...priceFor, text: e.target.value })} />
              <Button type="submit" variant="secondary" disabled={priceFor.text.trim() === ""}>{x("sell.priceAsk")}</Button>
              <Button variant="quiet" onClick={() => setPriceFor(null)}>{x("sell.priceNotNow")}</Button>
            </form>
          ) : (
            <>
              <p>{x("sell.priceNeedsManager")}</p>
              <ManagerApproval
                kv={kv} onCancel={() => setPriceFor(null)} onApproved={(managerId) => void approvePrice(managerId)}
                texts={{ who: x("sell.approvalWho"), pin: x("sell.approvalPin"), approve: x("sell.approvalApprove"), cancel: x("sell.priceNotNow"), wrong: (left) => x("sell.approvalWrong", { left }), locked: x("sell.approvalLocked"), nobody: x("sell.approvalNobody") }}
              />
            </>
          )}
        </div>
      )}

      {tipsOn && basket.entries.length > 0 && (
        <div className={styles.section} role="group" aria-label={x("sell.tip")}>
          <div className={styles.chips}>
            <Button variant={basket.tipCents === 0 ? "primary" : "secondary"} onClick={() => applyTip(0)}>{x("sell.noTip")}</Button>
            {[10, 15].map((p) => <Button key={p} variant="secondary" onClick={() => applyTip(Math.round((summary.totalCents * p) / 100))}>{x("sell.tipPercent", { percent: p })}</Button>)}
          </div>
          <form className={styles.row} onSubmit={(e) => { e.preventDefault(); submitTip(); }}>
            <input className={styles.input} inputMode="decimal" aria-label={x("sell.tipAmount")} placeholder={x("sell.tipAmount")} value={tipText} onChange={(e) => setTipText(e.target.value)} />
            <Button type="submit" variant="secondary">{x("sell.tipSet")}</Button>
          </form>
        </div>
      )}

      {customersOn && basket.entries.length > 0 && (
        <div className={styles.section} role="group" aria-label={x("sell.customer")}>
          {basket.customerId ? (
            <div className={styles.row}>
              <span>{x("sell.customerAttached", { name: customerName || "…" })}</span>
              <Button variant="quiet" onClick={detach}>{x("sell.customerRemove")}</Button>
            </div>
          ) : (
            <form className={styles.row} onSubmit={(e) => { e.preventDefault(); void findCustomer(); }}>
              <input className={styles.input} inputMode="tel" aria-label={x("sell.customer")} placeholder={x("sell.customer")} value={phone} onChange={(e) => setPhone(e.target.value)} />
              <Button type="submit" variant="secondary" disabled={phone.trim().length < 3}>{x("sell.customerFind")}</Button>
            </form>
          )}
          {found.length > 0 && (
            <ul className={styles.matches}>
              {found.map((c) => <li key={c.id}><Button variant="secondary" className={styles.match} onClick={() => attach(c)}>{c.name} <span>{c.phone}</span></Button></li>)}
            </ul>
          )}
        </div>
      )}

      <dl className={styles.totals}>
        {summary.discountCents > 0 && (<><dt>{t("sell.discount")}</dt><dd>−{rand(summary.discountCents)}</dd></>)}
        <dt>{t("sell.vat")}</dt><dd>{rand(summary.vatCents)}</dd>
        <dt className={styles.grand}>{t("sell.total")}</dt><dd className={styles.grand}>{rand(summary.totalCents)}</dd>
        {summary.tipCents > 0 && (<><dt>{x("sell.tip")}</dt><dd>{rand(summary.tipCents)}</dd><dt>{x("sell.due")}</dt><dd>{rand(summary.dueCents)}</dd></>)}
      </dl>

      <div className={styles.row}>
        <Button variant="quiet" disabled={basket.entries.length === 0} onClick={() => { setBasket(emptyBasket); setMessage(""); setCustomerName(""); setPriceFor(null); saleIdRef.current = null; }}>{t("sell.cancelSale")}</Button>
        <Button disabled={basket.entries.length === 0} onClick={startPay}>{t("sell.pay")} {rand(summary.dueCents)}</Button>
      </div>
    </section>
  );
}
