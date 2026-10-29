import type { Result } from "@tillknowit/domain";
import { formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextAreaField, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type ReorderingKey } from "./messages";
import type { Order } from "./types";

type Panel = { orderId: string; kind: "send" | "receive" | "rate" | "dispute" };

const statusKeys: Record<string, ReorderingKey> = {
  draft: "status_draft", sent: "status_sent", confirmed: "status_confirmed", delivered: "status_delivered", cancelled: "status_cancelled", disputed: "status_disputed",
};
const payKeys: Record<string, ReorderingKey> = { owing: "pay_owing", held: "pay_held", paid: "pay_paid", refunded: "pay_refunded" };

export function Orders({ refreshKey = 0 }: { refreshKey?: number }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const list = useLoad(() => api.get<Order[]>(`/shops/${shopId}/purchase-orders`, { callerPersonId: personId }), [shopId, personId, refreshKey]);
  const [panel, setPanel] = useState<Panel | null>(null);
  const [message, setMessage] = useState("");
  const [checkout, setCheckout] = useState<string | null>(null);

  const [channel, setChannel] = useState("app");
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [score, setScore] = useState("5");
  const [onTime, setOnTime] = useState(true);
  const [complete, setComplete] = useState(true);
  const [comment, setComment] = useState("");
  const [reason, setReason] = useState("");
  const [formError, setFormError] = useState("");

  const act = useAction((call: () => Promise<Result<unknown>>) => call());

  async function run(call: () => Promise<Result<unknown>>, success: string) {
    setMessage("");
    setCheckout(null);
    const r = await act.run(call);
    if (r.ok) {
      setMessage(success);
      setPanel(null);
      list.reload();
    }
    return r;
  }

  const open = (orderId: string, kind: Panel["kind"]) => { setPanel({ orderId, kind }); setFormError(""); act.clear(); };

  async function pay(o: Order) {
    const r = await run(() => api.post<{ checkoutUrl: string | null }>(`/purchase-orders/${o.id}/payments`, { callerPersonId: personId, returnUrl: window.location.href }), "");
    if (r.ok) {
      const url = (r.value as { checkoutUrl?: string | null } | undefined)?.checkoutUrl ?? null;
      setMessage("");
      setCheckout(url);
    }
  }

  function submitRate(o: Order) {
    const n = Number(score);
    if (!Number.isInteger(n) || n < 1 || n > 5) { setFormError(t("rateScoreInvalid")); return; }
    setFormError("");
    void run(() => api.post(`/purchase-orders/${o.id}/rating`, { callerPersonId: personId, onTime, complete, score: n, comment: comment.trim() === "" ? null : comment.trim() }), t("ratedOk"));
  }

  function submitDispute(o: Order) {
    if (reason.trim() === "") { setFormError(t("disputeReasonRequired")); return; }
    setFormError("");
    void run(() => api.post(`/purchase-orders/${o.id}/disputes`, { callerPersonId: personId, reason: reason.trim() }), t("disputedOk"));
  }

  function submitReceive(o: Order) {
    const receivedCases: Record<string, number> = {};
    for (const l of o.lines) {
      const raw = counts[l.id];
      receivedCases[l.id] = raw === undefined || raw.trim() === "" ? l.effectiveCases : Math.max(0, Math.floor(Number(raw)) || 0);
    }
    void run(() => api.post(`/purchase-orders/${o.id}/receive`, { callerPersonId: personId, receivedCases }), t("receivedOk", { ref: o.reference }));
  }

  const canCancel = (o: Order) => o.status === "draft" || o.status === "sent" || o.status === "confirmed";
  const canReceive = (o: Order) => (o.status === "confirmed" || o.status === "delivered") && !o.receivedAt;
  const canPay = (o: Order) => o.paymentStatus === "owing" && o.status !== "cancelled" && o.status !== "draft";

  const current = panel ? (list.data ?? []).find((o) => o.id === panel.orderId) ?? null : null;

  return (
    <Stack>
      <Card title={t("ordersHeading")}>
        <Stack>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(o) => o.id}
              empty={t("ordersEmpty")}
              columns={[
                { header: t("colReference"), cell: (o) => o.reference },
                { header: t("colSupplier"), cell: (o) => o.supplierName },
                { header: t("colStatus"), cell: (o) => <Inline><Badge>{statusKeys[o.status] ? t(statusKeys[o.status]!) : o.status}</Badge>{o.hasChanges && <Badge>{t("hasChanges")}</Badge>}</Inline> },
                { header: t("colPayment"), cell: (o) => (payKeys[o.paymentStatus] ? t(payKeys[o.paymentStatus]!) : o.paymentStatus) },
                { header: t("colTotal"), align: "right", cell: (o) => formatRand(o.totalCents) },
                {
                  header: t("colActions"),
                  cell: (o) => (
                    <Inline>
                      {o.status === "draft" && <Button variant="secondary" aria-label={t("sendFor", { ref: o.reference })} onClick={() => open(o.id, "send")}>{t("send")}</Button>}
                      {canReceive(o) && <Button variant="secondary" aria-label={t("receiveFor", { ref: o.reference })} onClick={() => open(o.id, "receive")}>{t("receive")}</Button>}
                      {canPay(o) && o.paymentMethod === "in_app" && <Button variant="secondary" disabled={act.busy} aria-label={t("payNowFor", { ref: o.reference })} onClick={() => void pay(o)}>{t("payNow")}</Button>}
                      {canPay(o) && o.paymentMethod !== "in_app" && (
                        <Button variant="secondary" disabled={act.busy} aria-label={t("markPaidFor", { ref: o.reference })} onClick={() => void run(() => api.post(`/purchase-orders/${o.id}/mark-paid`, undefined, { callerPersonId: personId }), t("paidOk"))}>{t("markPaid")}</Button>
                      )}
                      {o.receivedAt && <Button variant="secondary" aria-label={t("rateFor", { ref: o.reference })} onClick={() => open(o.id, "rate")}>{t("rate")}</Button>}
                      {(o.status === "delivered" || o.receivedAt) && o.status !== "disputed" && <Button variant="quiet" aria-label={t("disputeFor", { ref: o.reference })} onClick={() => open(o.id, "dispute")}>{t("dispute")}</Button>}
                      {canCancel(o) && <Button variant="quiet" disabled={act.busy} aria-label={t("cancelFor", { ref: o.reference })} onClick={() => void run(() => api.post(`/purchase-orders/${o.id}/cancel`, undefined, { callerPersonId: personId }), t("cancelledOk"))}>{t("cancel")}</Button>}
                    </Inline>
                  ),
                },
              ]}
            />
          )}
          {act.error && <Notice kind="danger">{act.error.message}</Notice>}
          {message && <Notice kind="success">{message}</Notice>}
          {checkout && <Notice kind="info"><a href={checkout}>{t("checkout")}</a></Notice>}
        </Stack>
      </Card>

      {panel && current && panel.kind === "send" && (
        <Card title={t("sendFor", { ref: current.reference })}>
          <form onSubmit={(e) => { e.preventDefault(); void run(() => api.post(`/purchase-orders/${current.id}/send`, { callerPersonId: personId, channel }), t("sentOk", { ref: current.reference })); }}>
            <Inline>
              <SelectField label={t("channel")} value={channel} onChange={(e) => setChannel(e.target.value)}>
                <option value="app">{t("channelApp")}</option>
                <option value="whatsapp">{t("channelWhatsapp")}</option>
                <option value="email">{t("channelEmail")}</option>
              </SelectField>
              <Button type="submit" disabled={act.busy}>{t("send")}</Button>
              <Button variant="quiet" onClick={() => setPanel(null)}>{t("close")}</Button>
            </Inline>
          </form>
        </Card>
      )}

      {panel && current && panel.kind === "receive" && (
        <Card title={t("receiveHeading", { ref: current.reference })}>
          <form onSubmit={(e) => { e.preventDefault(); submitReceive(current); }}>
            <Stack>
              {current.lines.map((l) => (
                <TextField
                  key={l.id}
                  label={t("receiveLine", { name: l.name, n: l.effectiveCases })}
                  inputMode="numeric"
                  value={counts[l.id] ?? String(l.effectiveCases)}
                  onChange={(e) => setCounts((c) => ({ ...c, [l.id]: e.target.value }))}
                />
              ))}
              <Inline>
                <Button type="submit" disabled={act.busy}>{t("receiveSave")}</Button>
                <Button variant="quiet" onClick={() => setPanel(null)}>{t("close")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}

      {panel && current && panel.kind === "rate" && (
        <Card title={t("rateFor", { ref: current.reference })}>
          <form onSubmit={(e) => { e.preventDefault(); submitRate(current); }} noValidate>
            <Stack>
              <TextField label={t("rateScore")} inputMode="numeric" value={score} error={formError} onChange={(e) => setScore(e.target.value)} />
              <CheckField label={t("rateOnTime")} checked={onTime} onChange={(e) => setOnTime(e.target.checked)} />
              <CheckField label={t("rateComplete")} checked={complete} onChange={(e) => setComplete(e.target.checked)} />
              <TextAreaField label={t("rateComment")} value={comment} onChange={(e) => setComment(e.target.value)} />
              <Inline>
                <Button type="submit" disabled={act.busy}>{t("rateSave")}</Button>
                <Button variant="quiet" onClick={() => setPanel(null)}>{t("close")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}

      {panel && current && panel.kind === "dispute" && (
        <Card title={t("disputeFor", { ref: current.reference })}>
          <form onSubmit={(e) => { e.preventDefault(); submitDispute(current); }} noValidate>
            <Stack>
              <TextAreaField label={t("disputeReason")} value={reason} error={formError} onChange={(e) => setReason(e.target.value)} />
              <Inline>
                <Button type="submit" disabled={act.busy}>{t("disputeSave")}</Button>
                <Button variant="quiet" onClick={() => setPanel(null)}>{t("close")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}
    </Stack>
  );
}
