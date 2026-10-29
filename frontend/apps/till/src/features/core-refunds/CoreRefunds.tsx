import { Badge, Button, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { cancellationRecord, creditCancelRecord, formatRand, newId, queueBuilt, refundRecord } from "@tillknowit/domain";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ManagerApproval, useBusy, useT, wholeNumber, type Translate } from "../../coreKit";
import type { TillContext } from "../../feature";
import { addRefunded, logCash, recentSales, refundAmount, refundableQuantity, type LoggedSale } from "../../ledger";
import { messages, type RefundsKey } from "./messages";

/** What the shop sets as the refund a cashier cannot approve alone, when its pack does not say: R500, like the server. */
export const DEFAULT_APPROVAL_THRESHOLD_CENTS = 50_000;

export function approvalThreshold(settingsJson: string | null): number {
  try {
    const value = (JSON.parse(settingsJson ?? "") as { approvalThresholdCents?: unknown }).approvalThresholdCents;
    return typeof value === "number" && Number.isInteger(value) && value >= 0 ? value : DEFAULT_APPROVAL_THRESHOLD_CENTS;
  } catch {
    return DEFAULT_APPROVAL_THRESHOLD_CENTS;
  }
}

const refundable = (method: string): boolean => method === "cash" || method === "card" || method === "qr" || method === "credit";
const clock = (iso: string) => new Date(iso).toLocaleString("en-ZA", { dateStyle: "short", timeStyle: "short" });

function methodText(t: Translate<RefundsKey>, method: string): string {
  const key = `method.${method}` as RefundsKey;
  return key in messages.en ? t(key) : t("method.other");
}

/**
 * Refunds and cancellations of a sale made on this till. The till finds the sale in its own books (the sales rung up here recently), so this works with no
 * connection: the refund or cancellation is queued and sent like a sale. The server checks the same rules again when it arrives.
 */
export function CoreRefunds({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  const [sales, setSales] = useState<LoggedSale[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [filter, setFilter] = useState("");
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => setSales(ctx.kv ? (await recentSales(ctx.kv)).reverse() : []), [ctx.kv]);
  useEffect(() => {
    void load();
  }, [load]);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return (sales ?? []).filter((s) => q === "" || s.lines.some((l) => l.name.toLowerCase().includes(q)) || formatRand(s.totalCents).replace(/\s/g, "").includes(q.replace(/\s/g, "")) || (s.totalCents / 100).toFixed(2).includes(q));
  }, [sales, filter]);

  const sale = sales?.find((s) => s.id === selected) ?? null;

  if (sale) {
    return (
      <Stack>
        <Button variant="quiet" onClick={() => { setSelected(null); void load(); }}>{t("back")}</Button>
        <SaleDetail ctx={ctx} t={t} sale={sale} onDone={async (text) => { setNotice(text); setSelected(null); await load(); }} />
      </Stack>
    );
  }

  return (
    <Stack>
      {notice && <Notice kind="success">{notice}</Notice>}
      {sales === null ? null : sales.length === 0 ? <p>{t("empty")}</p> : (
        <>
          <TextField label={t("find")} value={filter} onChange={(e) => setFilter(e.target.value)} />
          <Table
            rows={shown} rowKey={(s) => s.id} empty={t("noMatch")}
            columns={[
              { header: t("col.time"), cell: (s) => clock(s.soldAt) },
              { header: t("col.items"), cell: (s) => s.lines.map((l) => l.name).join(", ") },
              { header: t("col.total"), align: "right", cell: (s) => formatRand(s.totalCents + s.tipCents) },
              { header: t("col.paid"), cell: (s) => <>{methodText(t, s.paymentMethodCode)} {s.cancelled && <Badge>{t("cancelled")}</Badge>}</> },
              { header: t("col.choose"), cell: (s) => <Button variant="secondary" aria-label={t("open", { time: clock(s.soldAt) })} onClick={() => { setSelected(s.id); setNotice(null); }}>{t("col.choose")}</Button> },
            ]}
          />
        </>
      )}
    </Stack>
  );
}

function SaleDetail({ ctx, t, sale, onDone }: { ctx: TillContext; t: Translate<RefundsKey>; sale: LoggedSale; onDone: (text: string) => Promise<void> }) {
  const refundsOn = ctx.features.isOn("refunds");
  const mayCancel = ctx.staff.role !== "cashier";
  const threshold = approvalThreshold(ctx.features.settings("refunds"));
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [reason, setReason] = useState("");
  const [cancelling, setCancelling] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [asking, setAsking] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const { busy, run } = useBusy();

  const asked = sale.lines.map((l) => ({ productId: l.productId, quantity: wholeNumber(quantities[l.productId] ?? "") ?? 0 }));
  const tooMany = sale.lines.some((l, i) => (asked[i]?.quantity ?? 0) > refundableQuantity(sale, l));
  const amount = refundAmount(sale, asked);
  const needsApproval = amount > threshold;
  const canApproveSelf = ctx.staff.role !== "cashier";
  const anythingLeft = sale.lines.some((l) => refundableQuantity(sale, l) > 0);
  const anyRefunded = Object.values(sale.refunded).some((q) => q > 0);

  async function refund(approvedBy: string | null) {
    setMessage(null);
    const now = new Date();
    const built = refundRecord({ saleId: sale.id, reason, methodCode: null, approvedBy, lines: asked }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now });
    if (!built.ok) return setMessage(built.message);
    await queueBuilt(ctx.outbox, "refund", built, now);
    ctx.nudgeSync();
    await onDone(t("refund.saved", { amount: formatRand(amount) }));
  }

  async function startRefund() {
    if (asked.every((a) => a.quantity === 0)) return setMessage(t("refund.nothing"));
    if (tooMany) return setMessage(t("refund.tooMany"));
    if (reason.trim() === "") return setMessage(t("refund.reasonNeeded"));
    if (needsApproval && !canApproveSelf) return setAsking(true);
    await run(() => refund(needsApproval ? ctx.staff.id : null));
  }

  async function cancel() {
    setMessage(null);
    const now = new Date();
    const built = cancellationRecord({ saleId: sale.id, reason: cancelReason }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now });
    if (!built.ok) return setMessage(built.message);
    await queueBuilt(ctx.outbox, "cancellation", built, now);
    // A credit sale also put an entry in the customer's account. The cancelled sale takes it out again, queued just after the cancellation so it arrives second.
    if (sale.accountId) {
      const later = new Date(now.getTime() + 1);
      await queueBuilt(ctx.outbox, "credit_entry", creditCancelRecord({ accountId: sale.accountId, saleId: sale.id, totalCents: sale.totalCents }, { id: newId(later.getTime()), staffId: ctx.staff.id, at: later }), later);
    }
    ctx.nudgeSync();
    await onDone(t("cancel.saved"));
  }

  return (
    <Stack>
      <h2>{t("sale.heading", { total: formatRand(sale.totalCents + sale.tipCents), time: clock(sale.soldAt) })}</h2>
      <p>{t("sale.paidBy", { method: methodText(t, sale.paymentMethodCode) })}</p>
      {sale.cancelled ? <Notice kind="info">{t("cancelled")}</Notice> : (
        <>
          <h3>{t("refund.heading")}</h3>
          {!refundsOn ? <p>{t("refund.off")}</p> : !refundable(sale.paymentMethodCode) ? <OnlineRefund ctx={ctx} t={t} sale={sale} threshold={threshold} onDone={onDone} /> : !anythingLeft ? <p>{t("refund.nothingLeft")}</p> : asking ? (
            <Stack>
              <p>{t("refund.needsManager", { amount: formatRand(threshold) })}</p>
              <ManagerApproval
                kv={ctx.kv} onCancel={() => setAsking(false)}
                onApproved={(managerId) => { setAsking(false); void run(() => refund(managerId)); }}
                texts={{ who: t("approval.who"), pin: t("approval.pin"), approve: t("approval.approve"), cancel: t("approval.cancel"), wrong: (left) => t("approval.wrong", { left }), locked: t("approval.locked"), nobody: t("approval.nobody") }}
              />
            </Stack>
          ) : (
            <Stack>
              {sale.lines.map((l) => {
                const max = refundableQuantity(sale, l);
                return max === 0 ? null : (
                  <TextField
                    key={l.productId} label={t("refund.quantity", { name: l.name, max })} inputMode="numeric" value={quantities[l.productId] ?? ""}
                    onChange={(e) => setQuantities({ ...quantities, [l.productId]: e.target.value })}
                  />
                );
              })}
              <TextField label={t("refund.reason")} value={reason} onChange={(e) => setReason(e.target.value)} />
              <p>{t("refund.amount", { amount: formatRand(amount) })}</p>
              <p>{sale.paymentMethodCode === "credit" ? t("refund.goesCredit") : t("refund.goesBack", { method: methodText(t, sale.paymentMethodCode) })}</p>
              {needsApproval && !canApproveSelf && <p>{t("refund.needsManager", { amount: formatRand(threshold) })}</p>}
              <Button disabled={busy} onClick={() => void startRefund()}>{needsApproval && !canApproveSelf ? t("refund.approve") : t("refund.save")}</Button>
            </Stack>
          )}

          {mayCancel && (
            <>
              <h3>{t("cancel.heading")}</h3>
              {anyRefunded ? <p>{t("cancel.afterRefund")}</p> : !cancelling ? <Button variant="secondary" onClick={() => setCancelling(true)}>{t("cancel.start")}</Button> : (
                <Stack>
                  <TextField label={t("cancel.reason")} value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
                  <Button disabled={busy || cancelReason.trim() === ""} onClick={() => void run(cancel)}>{t("cancel.confirm")}</Button>
                </Stack>
              )}
            </>
          )}
        </>
      )}
      {message && <Notice kind="danger">{message}</Notice>}
    </Stack>
  );
}

/** What the server's online refund answers (OnlineRefundResponse in ShopCore.Contracts). The voucher code is shown once. */
interface OnlineRefundAnswer {
  refundId: string;
  amountCents: number;
  methodCode: string;
  voucher: { id: string; code: string; balanceCents: number; expiresOn: string | null } | null;
}

const onlineMethods = ["voucher", "cash", "card", "qr"] as const;

/**
 * A split, wallet, voucher or lay-by sale cannot be refunded through the offline upload (the server refuses it as refund.online_only). This sends the refund to
 * POST /shops/{shopId}/sales/{saleId}/refunds, which needs a connection and a sale the server already has. Unless told otherwise the money goes back as a gift voucher.
 */
function OnlineRefund({ ctx, t, sale, threshold, onDone }: { ctx: TillContext; t: Translate<RefundsKey>; sale: LoggedSale; threshold: number; onDone: (text: string) => Promise<void> }) {
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [reason, setReason] = useState("");
  const [method, setMethod] = useState<(typeof onlineMethods)[number]>("voucher");
  const [asking, setAsking] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  // One id per refund: pressing again after a lost answer is the same refund, never a second one.
  const refundId = useRef<string | null>(null);
  const { busy, run } = useBusy();

  const asked = sale.lines.map((l) => ({ productId: l.productId, quantity: wholeNumber(quantities[l.productId] ?? "") ?? 0 }));
  const tooMany = sale.lines.some((l, i) => (asked[i]?.quantity ?? 0) > refundableQuantity(sale, l));
  const amount = refundAmount(sale, asked);
  const needsApproval = amount > threshold;
  const isCashier = ctx.staff.role === "cashier";
  const device = ctx.device;

  if (!ctx.online) return <Notice kind="warning">{t("online.offline")}</Notice>;
  if (!device) return <Notice kind="warning">{t("online.noDevice")}</Notice>;
  if (!sale.lines.some((l) => refundableQuantity(sale, l) > 0)) return <p>{t("refund.nothingLeft")}</p>;

  async function send(approvedBy: string | null) {
    if (!device) return;
    setMessage(null);
    refundId.current ??= newId();
    const lines = asked.filter((a) => a.quantity > 0);
    const r = await ctx.api.post<OnlineRefundAnswer>(`/shops/${device.shopId}/sales/${sale.id}/refunds`, {
      deviceId: device.deviceId, staffId: ctx.staff.id, id: refundId.current, reason: reason.trim(), methodCode: method, approvedBy, lines,
    });
    if (!r.ok) {
      const key = `online.${r.code.replace(/^refund\./, "")}` as RefundsKey;
      return setMessage(key in messages.en ? t(key) : r.message);
    }
    refundId.current = null;
    // The till's own books follow, as they do for a refund sent from the queue.
    if (ctx.kv) {
      await addRefunded(ctx.kv, sale.id, lines);
      if (r.value.methodCode === "cash") await logCash(ctx.kv, { at: new Date().toISOString(), cents: -r.value.amountCents, kind: "refund" });
    }
    const voucher = r.value.voucher ? ` ${t("online.voucher", { code: r.value.voucher.code })}` : "";
    await onDone(`${t("online.saved", { amount: formatRand(r.value.amountCents) })}${voucher}`);
  }

  async function start() {
    if (asked.every((a) => a.quantity === 0)) return setMessage(t("refund.nothing"));
    if (tooMany) return setMessage(t("refund.tooMany"));
    if (reason.trim() === "") return setMessage(t("refund.reasonNeeded"));
    if (needsApproval && isCashier) return setAsking(true);
    await run(() => send(needsApproval ? ctx.staff.id : null));
  }

  return (
    <Stack>
      <Notice kind="info">{t("refund.online")}</Notice>
      {asking ? (
        <Stack>
          <p>{t("refund.needsManager", { amount: formatRand(threshold) })}</p>
          <ManagerApproval
            kv={ctx.kv} onCancel={() => setAsking(false)}
            onApproved={(managerId) => { setAsking(false); void run(() => send(managerId)); }}
            texts={{ who: t("approval.who"), pin: t("approval.pin"), approve: t("approval.approve"), cancel: t("approval.cancel"), wrong: (left) => t("approval.wrong", { left }), locked: t("approval.locked"), nobody: t("approval.nobody") }}
          />
        </Stack>
      ) : (
        <Stack>
          {sale.lines.map((l) => {
            const max = refundableQuantity(sale, l);
            return max === 0 ? null : (
              <TextField key={l.productId} label={t("refund.quantity", { name: l.name, max })} inputMode="numeric" value={quantities[l.productId] ?? ""} onChange={(e) => setQuantities({ ...quantities, [l.productId]: e.target.value })} />
            );
          })}
          <TextField label={t("refund.reason")} value={reason} onChange={(e) => setReason(e.target.value)} />
          <SelectField label={t("online.method")} value={method} onChange={(e) => setMethod(e.target.value as (typeof onlineMethods)[number])}>
            {onlineMethods.map((m) => <option key={m} value={m}>{t(`online.method.${m}` as RefundsKey)}</option>)}
          </SelectField>
          <p>{t("refund.amount", { amount: formatRand(amount) })}</p>
          {needsApproval && isCashier && <p>{t("refund.needsManager", { amount: formatRand(threshold) })}</p>}
          <Button disabled={busy} onClick={() => void start()}>{needsApproval && isCashier ? t("refund.approve") : t("online.button")}</Button>
        </Stack>
      )}
      {message && <Notice kind="danger">{message}</Notice>}
    </Stack>
  );
}
