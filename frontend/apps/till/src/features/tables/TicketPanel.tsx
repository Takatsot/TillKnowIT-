import { Badge, Button, Card, Inline, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, useAction, useKitT, type Caller, type Translate } from "../till-kit/kit";
import { LinesEditor } from "../till-kit/LinesEditor";
import type { TicketLineDto, TicketResponse } from "../till-kit/types";
import type { TablesKey } from "./messages";
import { PayPanel } from "../till-kit/PayPanel";

export interface FloorTable {
  id: string;
  area: string;
  name: string;
  seats: number;
}

interface Props {
  ctx: TillContext;
  caller: Caller;
  ticket: TicketResponse;
  tables: readonly FloorTable[];
  others: readonly TicketResponse[];
  t: Translate<TablesKey>;
  /** Something changed on the server: reload the lists. */
  changed: () => void;
  /** Leave this ticket, with an optional sentence to show on the list. */
  leave: (text?: string) => void;
}

const base = (c: Caller) => `/shops/${c.shopId}/tickets`;
const pair = (c: Caller) => ({ deviceId: c.deviceId, staffId: c.staffId });

/** One ticket: its items, the hold that lets this till change it, and what can be done with it (move, merge, pay, void). */
export function TicketPanel({ ctx, caller, ticket: initial, tables, others, t, changed, leave }: Props) {
  const kit = useKitT(ctx.language);
  const [ticket, setTicket] = useState(initial);
  const [lines, setLines] = useState<TicketLineDto[]>(initial.lines);
  const [tableId, setTableId] = useState(initial.tableId ?? "");
  const [otherId, setOtherId] = useState("");
  const [paying, setPaying] = useState(false);
  const [sureVoid, setSureVoid] = useState(false);
  const [info, setInfo] = useState("");
  const action = useAction();

  const mine = ticket.heldByDeviceId === caller.deviceId && !ticket.lockedForYou;
  const dirty = JSON.stringify(lines) !== JSON.stringify(ticket.lines);

  function take(next: TicketResponse, text = "") {
    setTicket(next);
    setLines(next.lines);
    setTableId(next.tableId ?? "");
    setInfo(text);
    changed();
  }

  const hold = async () => {
    const r = await action.run(() => ctx.api.post<TicketResponse>(`${base(caller)}/${ticket.id}/hold`, pair(caller)));
    if (r.ok) take(r.value);
  };
  const release = async () => {
    const r = await action.run(() => ctx.api.post<void>(`${base(caller)}/${ticket.id}/release`, pair(caller)));
    if (r.ok) {
      setTicket({ ...ticket, heldByDeviceId: null, heldUntil: null });
      setInfo(t("ticket.released"));
      changed();
    }
  };
  const save = async () => {
    const r = await action.run(() => ctx.api.put<TicketResponse>(`${base(caller)}/${ticket.id}`, { ...pair(caller), lines }));
    if (r.ok) take(r.value, t("ticket.saved"));
  };
  const move = async () => {
    const r = await action.run(() => ctx.api.post<TicketResponse>(`${base(caller)}/${ticket.id}/move`, { ...pair(caller), tableId: tableId === "" ? null : tableId }));
    if (r.ok) take(r.value);
  };
  /** Merging needs both tickets held by this till, so the other one is taken first. */
  const merge = async () => {
    if (otherId === "") return;
    const held = await action.run(() => ctx.api.post<TicketResponse>(`${base(caller)}/${otherId}/hold`, pair(caller)));
    if (!held.ok) return;
    const r = await action.run(() => ctx.api.post<TicketResponse>(`${base(caller)}/${ticket.id}/merge`, { ...pair(caller), otherTicketId: otherId }));
    if (r.ok) {
      setOtherId("");
      take(r.value);
    }
  };
  const voidIt = async () => {
    const r = await action.run(() => ctx.api.post<void>(`${base(caller)}/${ticket.id}/void`, pair(caller)));
    if (r.ok) {
      changed();
      leave(t("ticket.voided"));
    }
  };

  if (paying) {
    return <PayPanel ctx={ctx} caller={caller} ticket={ticket} onBack={() => setPaying(false)} onPaid={(text) => { changed(); leave(text); }} />;
  }

  return (
    <Card title={t("ticket.title", { label: ticket.label })}>
      <Stack>
        <Inline>
          <Badge>{mine ? t("state.mine") : ticket.lockedForYou ? t("state.locked") : t("state.free")}</Badge>
          <Button variant="quiet" onClick={() => leave()}>{t("ticket.back")}</Button>
        </Inline>
        {ticket.lockedForYou ? <Notice kind="warning">{t("ticket.readOnly")}</Notice> : null}
        {!mine && !ticket.lockedForYou ? <Notice>{t("ticket.needHold")}</Notice> : null}
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={action.error} t={kit} />

        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} readOnly={!mine} allowNotes />

        <Inline>
          {!mine && !ticket.lockedForYou ? <Button onClick={() => void hold()} disabled={action.busy}>{t("ticket.hold")}</Button> : null}
          {mine && dirty ? <Button onClick={() => void save()} disabled={action.busy}>{t("ticket.save")}</Button> : null}
          {mine ? <Button variant="secondary" onClick={() => void release()} disabled={action.busy || dirty}>{t("ticket.release")}</Button> : null}
          {!ticket.lockedForYou ? <Button onClick={() => (lines.length === 0 ? setInfo(t("ticket.empty")) : setPaying(true))} disabled={action.busy || dirty}>{t("ticket.pay")}</Button> : null}
        </Inline>

        {mine ? (
          <>
            <Inline>
              <SelectField label={t("ticket.move")} value={tableId} onChange={(e) => setTableId(e.target.value)}>
                <option value="">{t("ticket.moveNone")}</option>
                {tables.map((x) => <option key={x.id} value={x.id}>{x.area ? `${x.area} ${x.name}` : x.name}</option>)}
              </SelectField>
              <Button variant="secondary" onClick={() => void move()} disabled={action.busy}>{t("ticket.moveButton")}</Button>
            </Inline>
            {others.length === 0 ? <p>{t("ticket.mergeNone")}</p> : (
              <Inline>
                <SelectField label={t("ticket.merge")} value={otherId} onChange={(e) => setOtherId(e.target.value)}>
                  <option value="">{t("ticket.mergeChoose")}</option>
                  {others.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
                </SelectField>
                <Button variant="secondary" onClick={() => void merge()} disabled={action.busy || otherId === ""}>{t("ticket.mergeButton")}</Button>
              </Inline>
            )}
          </>
        ) : null}

        {!ticket.lockedForYou ? (
          sureVoid ? (
            <Notice kind="warning">
              <Stack>
                {t("ticket.voidSure", { label: ticket.label })}
                <Inline>
                  <Button variant="secondary" onClick={() => void voidIt()} disabled={action.busy}>{t("ticket.voidYes")}</Button>
                  <Button variant="quiet" onClick={() => setSureVoid(false)}>{t("ticket.voidNo")}</Button>
                </Inline>
              </Stack>
            </Notice>
          ) : <Button variant="quiet" onClick={() => setSureVoid(true)}>{t("ticket.void")}</Button>
        ) : null}
      </Stack>
    </Card>
  );
}
