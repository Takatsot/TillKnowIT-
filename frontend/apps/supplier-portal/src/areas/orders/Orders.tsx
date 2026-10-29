import { formatDate, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextAreaField, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface OrderLine {
  id: string; name: string; unitsPerCase: number; orderedCases: number; confirmedCases: number | null; deliveredCases: number | null; effectiveCases: number;
  casePriceCents: number; status: string;
}
export interface Order {
  id: string; reference: string; status: string; deliveryDate: string | null; paymentMethod: string; paymentStatus: string; lines: OrderLine[];
  subtotalCents: number; vatCents: number; deliveryFeeCents: number; totalCents: number; belowMinimum: boolean; minOrderCents: number;
}

type Decision = { action: "confirm" | "change" | "decline"; cases: string; price: string };

function Respond({ order, onDone }: { order: Order; onDone: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [decisions, setDecisions] = useState<Record<string, Decision>>(() => Object.fromEntries(order.lines.map((l) => [l.id, { action: "confirm", cases: String(l.orderedCases), price: "" } as Decision])));
  const [date, setDate] = useState(order.deliveryDate ?? "");
  const [problem, setProblem] = useState("");
  const send = useAction((body: object) => api.patch<Order>(`/purchase-orders/${order.id}/lines`, body));
  const set = (id: string, patch: Partial<Decision>) => setDecisions({ ...decisions, [id]: { ...decisions[id]!, ...patch } });

  async function submit() {
    const out: { lineId: string; action: string; cases: number | null; casePriceCents: number | null }[] = [];
    for (const l of order.lines) {
      const d = decisions[l.id]!;
      if (d.action !== "change") { out.push({ lineId: l.id, action: d.action, cases: null, casePriceCents: null }); continue; }
      const cases = Number(d.cases);
      const price = d.price.trim() === "" ? null : parseRandToCents(d.price);
      if (!Number.isInteger(cases) || cases < 0 || (d.price.trim() !== "" && price === null)) { setProblem(t("badNumber")); return; }
      out.push({ lineId: l.id, action: "change", cases, casePriceCents: price });
    }
    setProblem("");
    const r = await send.run({ callerPersonId: personId, decisions: out, deliveryDate: date === "" ? null : date });
    if (r.ok) onDone(t("replied", { ref: order.reference }));
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <Stack>
        <h3>{t("respondHeading")}</h3>
        {order.lines.map((l) => (
          <Inline key={l.id}>
            <SelectField label={t("action", { name: l.name })} value={decisions[l.id]!.action} onChange={(e) => set(l.id, { action: e.target.value as Decision["action"] })}>
              <option value="confirm">{t("confirm")}</option>
              <option value="change">{t("change")}</option>
              <option value="decline">{t("decline")}</option>
            </SelectField>
            {decisions[l.id]!.action === "change" && (
              <>
                <TextField label={t("changeCases", { name: l.name })} inputMode="numeric" value={decisions[l.id]!.cases} onChange={(e) => set(l.id, { cases: e.target.value })} />
                <TextField label={t("changePrice", { name: l.name })} inputMode="decimal" value={decisions[l.id]!.price} onChange={(e) => set(l.id, { price: e.target.value })} />
              </>
            )}
          </Inline>
        ))}
        <Inline>
          <TextField label={t("deliveryDate")} type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          <Button type="submit" disabled={send.busy}>{t("sendReply")}</Button>
        </Inline>
        {problem && <Notice kind="danger">{problem}</Notice>}
        {send.error && <Notice kind="danger">{send.error.message}</Notice>}
      </Stack>
    </form>
  );
}

function Deliver({ order, onDone }: { order: Order; onDone: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [counts, setCounts] = useState<Record<string, string>>(() => Object.fromEntries(order.lines.filter((l) => l.status !== "declined").map((l) => [l.id, String(l.effectiveCases)])));
  const [problem, setProblem] = useState("");
  const send = useAction((body: object) => api.post<Order>(`/purchase-orders/${order.id}/deliver`, body));

  async function submit() {
    const short: Record<string, number> = {};
    for (const l of order.lines) {
      const raw = counts[l.id];
      if (raw === undefined) continue;
      const n = Number(raw);
      if (!Number.isInteger(n) || n < 0) { setProblem(t("badNumber")); return; }
      if (n !== l.effectiveCases) short[l.id] = n;
    }
    setProblem("");
    const r = await send.run({ callerPersonId: personId, deliveredCases: Object.keys(short).length === 0 ? null : short });
    if (r.ok) onDone(t("deliveredDone", { ref: order.reference }));
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <Stack>
        <h3>{t("deliverHeading")}</h3>
        <p>{t("deliverHint")}</p>
        <Inline>
          {order.lines.filter((l) => counts[l.id] !== undefined).map((l) => (
            <TextField key={l.id} label={t("deliveredCases", { name: l.name })} inputMode="numeric" value={counts[l.id]} onChange={(e) => setCounts({ ...counts, [l.id]: e.target.value })} />
          ))}
        </Inline>
        <div><Button type="submit" disabled={send.busy}>{t("deliver")}</Button></div>
        {problem && <Notice kind="danger">{problem}</Notice>}
        {send.error && <Notice kind="danger">{send.error.message}</Notice>}
      </Stack>
    </form>
  );
}

function Dispute({ order, onDone }: { order: Order; onDone: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [reason, setReason] = useState("");
  const send = useAction(() => api.post<unknown>(`/purchase-orders/${order.id}/disputes`, { callerPersonId: personId, reason: reason.trim() }));
  return (
    <form onSubmit={(e) => { e.preventDefault(); void send.run().then((r) => { if (r.ok) onDone(t("disputeDone")); }); }}>
      <Stack>
        <h3>{t("disputeHeading")}</h3>
        <p>{t("disputeNote")}</p>
        <TextAreaField label={t("disputeReason")} value={reason} onChange={(e) => setReason(e.target.value)} required />
        <div><Button type="submit" variant="secondary" disabled={send.busy || reason.trim() === ""}>{t("raise")}</Button></div>
        {send.error && <Notice kind="danger">{send.error.message}</Notice>}
      </Stack>
    </form>
  );
}

function OrderDetail({ order, onClose, onChanged }: { order: Order; onClose: () => void; onChanged: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const pay = useAction(() => api.post<void>(`/purchase-orders/${order.id}/mark-paid`, undefined, { callerPersonId: personId }));
  return (
    <Card title={t("detailHeading", { ref: order.reference })}>
      <Stack>
        <Inline><Badge>{order.status}</Badge><Badge>{order.paymentStatus}</Badge><Button variant="quiet" onClick={onClose}>{t("close")}</Button></Inline>
        {order.belowMinimum && <Notice kind="warning">{t("belowMinimum", { min: formatRand(order.minOrderCents) })}</Notice>}
        <Table
          rows={order.lines}
          rowKey={(l) => l.id}
          columns={[
            { header: t("colLine"), cell: (l) => l.name },
            { header: t("colOrdered"), cell: (l) => l.orderedCases, align: "right" },
            { header: t("colConfirmed"), cell: (l) => l.confirmedCases ?? "", align: "right" },
            { header: t("colDelivered"), cell: (l) => l.deliveredCases ?? "", align: "right" },
            { header: t("colPrice"), cell: (l) => formatRand(l.casePriceCents), align: "right" },
            { header: t("colLineStatus"), cell: (l) => l.status },
          ]}
        />
        <p>{t("total", { total: formatRand(order.totalCents), fee: formatRand(order.deliveryFeeCents), vat: formatRand(order.vatCents) })}</p>
        {order.status === "sent" && <Respond order={order} onDone={onChanged} />}
        {order.status === "confirmed" && <Deliver order={order} onDone={onChanged} />}
        {order.status === "delivered" && order.paymentStatus === "owing" && (
          <div>
            <Button variant="secondary" disabled={pay.busy} onClick={() => void pay.run().then((r) => { if (r.ok) onChanged(t("paidDone", { ref: order.reference })); })}>{t("markPaid")}</Button>
            {pay.error && <Notice kind="danger">{pay.error.message}</Notice>}
          </div>
        )}
        {order.status === "delivered" && <Dispute order={order} onDone={onChanged} />}
      </Stack>
    </Card>
  );
}

export function Orders({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [status, setStatus] = useState("");
  const list = useLoad(() => api.get<Order[]>(`/suppliers/${supplierId}/orders`, { callerPersonId: personId, status }), [supplierId, personId, status]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const open = list.data?.find((o) => o.id === openId) ?? null;

  return (
    <Stack>
      {note && <Notice kind="success">{note}</Notice>}
      {open && <OrderDetail key={open.id} order={open} onClose={() => setOpenId(null)} onChanged={(m) => { setNote(m); setOpenId(null); list.reload(); }} />}
      <Card title={t("ordersHeading")}>
        <SelectField label={t("statusFilter")} value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">{t("all")}</option>
          <option value="sent">{t("sent")}</option>
          <option value="confirmed">{t("confirmed")}</option>
          <option value="delivered">{t("delivered")}</option>
          <option value="disputed">{t("disputed")}</option>
          <option value="cancelled">{t("cancelled")}</option>
        </SelectField>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(o) => o.id}
            empty={t("ordersEmpty")}
            columns={[
              { header: t("colRef"), cell: (o) => o.reference },
              { header: t("colStatus"), cell: (o) => <Badge>{o.status}</Badge> },
              { header: t("colPayment"), cell: (o) => `${o.paymentMethod} / ${o.paymentStatus}` },
              { header: t("colDelivery"), cell: (o) => formatDate(o.deliveryDate) },
              { header: t("colTotal"), cell: (o) => formatRand(o.totalCents), align: "right" },
              { header: "", cell: (o) => <Button variant="secondary" aria-label={t("open", { ref: o.reference })} onClick={() => { setNote(""); setOpenId(o.id); }}>{t("openShort")}</Button> },
            ]}
          />
        )}
      </Card>
    </Stack>
  );
}
