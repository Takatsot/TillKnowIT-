import { formatDate, formatDateTime, formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { ChildScope, itemStyle, listStyle, mutedStyle, type ChildContext } from "../../shared/ChildScope";
import type { Menu, PreOrder } from "../../shared/types";
import { messages, type PreOrderKey } from "./messages";

export function PreOrders() {
  return <ChildScope render={(ctx) => <Body key={ctx.child.holderId} ctx={ctx} />} />;
}

function Body({ ctx }: { ctx: ChildContext }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const { child } = ctx;
  const menu = useLoad(() => api.get<Menu>(`/holders/${child.holderId}/menu`, { callerPersonId: personId }), [child.holderId, personId]);
  const orders = useLoad(() => api.get<PreOrder[]>(`/holders/${child.holderId}/pre-orders`, { callerPersonId: personId }), [child.holderId, personId]);
  const [breakCode, setBreakCode] = useState("");
  const [day, setDay] = useState(formatDate(new Date()));
  const [qty, setQty] = useState<Record<string, number>>({});
  const [note, setNote] = useState("");
  const [cancelNote, setCancelNote] = useState("");

  const items = menu.data?.items ?? [];
  const chosen = items.filter((i) => (qty[i.id] ?? 0) > 0);
  const total = chosen.reduce((sum, i) => sum + i.priceCents * (qty[i.id] ?? 0), 0);
  const selectedBreak = menu.data?.breaks.find((b) => b.code === breakCode) ?? menu.data?.breaks[0];

  const place = useAction(() => api.post<PreOrder>("/pre-orders", {
    callerPersonId: personId, holderId: child.holderId, breakCode: selectedBreak?.code ?? "", collectOn: day,
    lines: chosen.map((i) => ({ menuItemId: i.id, quantity: qty[i.id] ?? 0 })),
  }));
  const cancel = useAction((id: string) => api.post(`/pre-orders/${id}/cancel`, undefined, { callerPersonId: personId }));

  async function submit() {
    setNote("");
    const r = await place.run();
    if (r.ok) {
      setNote(t("placed", { amount: formatRand(r.value.totalCents) }));
      setQty({});
      orders.reload();
      ctx.reload();
    }
  }

  const breakName = (code: string) => menu.data?.breaks.find((b) => b.code === code)?.name ?? code;
  const statusText = (status: string) => {
    const key = `status${status.toLowerCase()}` as PreOrderKey;
    return key in messages.en ? t(key) : status;
  };

  return (
    <Stack>
      <Card title={t("placeHeading")}>
        <p style={mutedStyle}>{t("placeHelp")}</p>
        {menu.error && (
          <>
            <Notice kind="danger">{menu.error.message}</Notice>
            <Button variant="secondary" onClick={menu.reload}>{t("reload")}</Button>
          </>
        )}
        {!menu.data && !menu.error && <p>{t("loading")}</p>}
        {menu.data && (menu.data.breaks.length === 0 || items.length === 0) && <p>{t("noMenu")}</p>}
        {menu.data && menu.data.breaks.length > 0 && items.length > 0 && (
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <SelectField label={t("break")} value={selectedBreak?.code ?? ""} onChange={(e) => setBreakCode(e.target.value)}>
                {menu.data.breaks.map((b) => <option key={b.code} value={b.code}>{t("breakOption", { name: b.name, cutOff: b.cutOff })}</option>)}
              </SelectField>
              <TextField label={t("day")} hint={t("dayHint")} type="date" value={day} onChange={(e) => setDay(e.target.value)} required />
              <strong>{t("menu")}</strong>
              <ul style={listStyle}>
                {items.map((i) => (
                  <li key={i.id} style={itemStyle}>
                    <strong>{i.name}</strong>
                    <span style={mutedStyle}>{formatRand(i.priceCents)}</span>
                    <TextField
                      label={t("quantity", { name: i.name })} type="number" inputMode="numeric" min={0} max={20} value={qty[i.id] ?? 0}
                      onChange={(e) => setQty({ ...qty, [i.id]: Math.max(0, Math.min(20, Number(e.target.value) || 0)) })}
                    />
                  </li>
                ))}
              </ul>
              <strong>{chosen.length === 0 ? t("nothingChosen") : t("total", { amount: formatRand(total) })}</strong>
              {place.error && <Notice kind="danger">{place.error.message}</Notice>}
              {note && <Notice kind="success">{note}</Notice>}
              <Button type="submit" disabled={place.busy || chosen.length === 0}>{t("place")}</Button>
            </Stack>
          </form>
        )}
      </Card>

      <Card title={t("listHeading")}>
        {orders.error && (
          <>
            <Notice kind="danger">{orders.error.message}</Notice>
            <Button variant="secondary" onClick={orders.reload}>{t("reload")}</Button>
          </>
        )}
        {!orders.data && !orders.error && <p>{t("loading")}</p>}
        {orders.data && orders.data.length === 0 && <p>{t("listEmpty")}</p>}
        {orders.data && orders.data.length > 0 && (
          <ul style={listStyle}>
            {orders.data.map((o) => (
              <li key={o.id} style={itemStyle}>
                <strong>{t("orderTitle", { break: breakName(o.breakCode), day: formatDate(o.collectOn) })}</strong>
                <span><Badge>{statusText(o.status)}</Badge></span>
                <ul style={{ ...listStyle, gap: "var(--space-1)" }}>
                  {o.lines.map((l) => <li key={l.menuItemId}>{t("line", { quantity: l.quantity, name: l.name })}</li>)}
                </ul>
                <strong>{formatRand(o.totalCents)}</strong>
                {["placed", "ready"].includes(o.status.toLowerCase()) && (
                  <>
                    <span style={mutedStyle}>{t("cutOff", { time: formatDateTime(o.cutOffAt) })}</span>
                    <Button
                      variant="secondary" disabled={cancel.busy} aria-label={t("cancelLabel", { break: breakName(o.breakCode), day: formatDate(o.collectOn) })}
                      onClick={() => void cancel.run(o.id).then((r) => { if (r.ok) { setCancelNote(t("cancelled")); orders.reload(); ctx.reload(); } })}
                    >
                      {t("cancel")}
                    </Button>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
        {cancel.error && <Notice kind="danger">{cancel.error.message}</Notice>}
        {cancelNote && <Notice kind="success">{cancelNote}</Notice>}
      </Card>
    </Stack>
  );
}
