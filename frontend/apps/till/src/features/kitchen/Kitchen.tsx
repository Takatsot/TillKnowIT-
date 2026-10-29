import { Badge, Button, Card, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { newId } from "@tillknowit/domain";
import { useEffect, useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, suggestRef, TabGate, useAction, useKitT, useLoad, useT, who, type Caller, type Translate } from "../till-kit/kit";
import { divisorOf, LinesEditor } from "../till-kit/LinesEditor";
import { saTime } from "../till-kit/time";
import type { TicketLineDto, TicketResponse } from "../till-kit/types";
import { messages, type KitchenKey } from "./messages";
import { printKitchenOrder, type KitchenOrderView } from "./print";
import styles from "./Kitchen.module.css";

interface KitchenOrderResponse extends KitchenOrderView {
  status: string;
  readyAt: string | null;
}

const REFRESH_MS = 15_000;
const columns = [
  { status: "new", title: "col.new" as const, actions: ["start", "cancel"] },
  { status: "preparing", title: "col.preparing" as const, actions: ["ready", "cancel"] },
  { status: "ready", title: "col.ready" as const, actions: ["collect"] },
];

/** A fresh order number: "K" and five digits from the clock, so two tills rarely pick the same. The person can type their own. */
const suggestOrderNo = (): string => suggestRef("K");

export function Kitchen({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <KitchenBody ctx={ctx} caller={caller} />}</TabGate>;
}

function KitchenBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const [view, setView] = useState<"board" | "send">("board");
  const [info, setInfo] = useState("");
  return (
    <Card title={t("title")}>
      <Stack>
        <Inline>
          <Button variant={view === "board" ? "primary" : "secondary"} aria-pressed={view === "board"} onClick={() => setView("board")}>{t("view.board")}</Button>
          <Button variant={view === "send" ? "primary" : "secondary"} aria-pressed={view === "send"} onClick={() => setView("send")}>{t("view.send")}</Button>
        </Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        {view === "board" ? <Board ctx={ctx} caller={caller} t={t} setInfo={setInfo} /> : <Send ctx={ctx} caller={caller} t={t} sent={(text) => { setInfo(text); setView("board"); }} />}
      </Stack>
    </Card>
  );
}

function Board({ ctx, caller, t, setInfo }: { ctx: TillContext; caller: Caller; t: Translate<KitchenKey>; setInfo: (text: string) => void }) {
  const kit = useKitT(ctx.language);
  const orders = useLoad(() => ctx.api.get<KitchenOrderResponse[]>(`/shops/${caller.shopId}/kitchen-orders`, who(caller)), [caller.shopId]);
  const action = useAction();

  // The board refreshes by itself so the screen on the pass stays current.
  useEffect(() => {
    const timer = setInterval(orders.reload, REFRESH_MS);
    return () => clearInterval(timer);
  }, [orders.reload]);

  const advance = async (o: KitchenOrderResponse, act: string) => {
    const r = await action.run(() => ctx.api.post<KitchenOrderResponse>(`/shops/${caller.shopId}/kitchen-orders/${o.id}/advance`, { ...who(caller), action: act }));
    if (r.ok) orders.reload();
  };
  const print = async (o: KitchenOrderResponse) => {
    const out = await printKitchenOrder(ctx.hardware, o, "printer");
    setInfo(out.kind === "done" ? t("act.printed") : out.kind === "failed" ? out.reason : t("send.noDevice"));
  };

  const list = orders.data ?? [];
  return (
    <Stack>
      <Inline><Button variant="secondary" onClick={orders.reload}>{t("board.refresh")}</Button></Inline>
      <ErrorNotice error={orders.error ?? action.error} t={kit} />
      {orders.loading && orders.data === null ? <p>{kit("kit.loading")}</p> : null}
      {!orders.loading && !orders.error && list.length === 0 ? <p>{t("board.empty")}</p> : null}
      <div className={styles.board}>
        {columns.map((col) => {
          const here = list.filter((o) => o.status === col.status);
          return (
            <section key={col.status} aria-label={t(col.title)} className={styles.column}>
              <h3 className={styles.heading}>{t(col.title)} ({here.length})</h3>
              <ul className={styles.orders}>
                {here.map((o) => (
                  <li key={o.id} className={styles.order}>
                    <Inline>
                      <strong>{t("board.order", { no: o.orderNo })}</strong>
                      <Badge>{t(`status.${o.status}` as KitchenKey)}</Badge>
                    </Inline>
                    <span>{o.tableLabel ? t("board.table", { table: o.tableLabel }) : t("board.takeaway")}</span>
                    <span className={styles.muted}>{t("board.sent", { time: saTime(o.createdAt) })}{o.readyAt ? `, ${t("board.readyAt", { time: saTime(o.readyAt) })}` : ""}</span>
                    <ul className={styles.items}>
                      {o.items.map((i, n) => <li key={n}>{i.quantity} x {i.name}{i.note ? ` (${i.note})` : ""}</li>)}
                    </ul>
                    <Inline>
                      {col.actions.map((a) => (
                        <Button key={a} variant={a === "cancel" ? "quiet" : "primary"} disabled={action.busy} aria-label={t(`act.${a}` as KitchenKey, { no: o.orderNo })} onClick={() => void advance(o, a)}>
                          {t(`act.${a}` as KitchenKey, { no: o.orderNo })}
                        </Button>
                      ))}
                      {ctx.hardware.printer ? <Button variant="secondary" aria-label={t("act.print", { no: o.orderNo })} onClick={() => void print(o)}>{t("act.print", { no: o.orderNo })}</Button> : null}
                    </Inline>
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>
    </Stack>
  );
}

function Send({ ctx, caller, t, sent }: { ctx: TillContext; caller: Caller; t: Translate<KitchenKey>; sent: (text: string) => void }) {
  const kit = useKitT(ctx.language);
  const [orderNo, setOrderNo] = useState(() => suggestOrderNo());
  const [table, setTable] = useState("");
  const [lines, setLines] = useState<TicketLineDto[]>([]);
  const [ticketId, setTicketId] = useState("");
  const [problem, setProblem] = useState("");
  const [id, setId] = useState(() => newId());
  const action = useAction();
  const tickets = useLoad(() => (ctx.features.isOn("open_tickets") ? ctx.api.get<TicketResponse[]>(`/shops/${caller.shopId}/tickets`, who(caller)) : Promise.resolve({ ok: true as const, value: [] as TicketResponse[] })), [caller.shopId]);

  function fill(chosen: string) {
    setTicketId(chosen);
    const found = tickets.data?.find((x) => x.id === chosen);
    if (!found) return;
    setLines(found.lines.filter((l) => divisorOf(l) === 1));
    setTable(found.label);
  }

  async function send() {
    const no = orderNo.trim();
    if (no === "") return setProblem(t("send.orderNoRequired"));
    if (lines.length === 0) return setProblem(t("send.noItems"));
    setProblem("");
    const r = await action.run(() =>
      ctx.api.post<KitchenOrderResponse>(`/shops/${caller.shopId}/kitchen-orders`, {
        ...who(caller), id, orderNo: no, ticketId: ticketId || null, saleId: null, tableLabel: table.trim() || null,
        items: lines.map((l) => ({ name: l.name, quantity: l.quantity, note: l.note?.trim() || null })),
      }));
    if (!r.ok) {
      if (r.code === "kitchen.order_no_taken") {
        setOrderNo(suggestOrderNo());
        setId(newId());
        setProblem(t("send.taken"));
      }
      return;
    }
    const out = await printKitchenOrder(ctx.hardware, r.value);
    sent(out.kind === "done" ? t("send.sentPrinted", { no }) : out.kind === "failed" ? t("send.sentNotPrinted", { no, reason: out.reason }) : t("send.sent", { no }));
  }

  return (
    <Stack>
      <TextField label={t("send.orderNo")} hint={t("send.orderNoHint")} maxLength={12} value={orderNo} onChange={(e) => setOrderNo(e.target.value)} />
      {(tickets.data ?? []).length > 0 ? (
        <SelectField label={t("send.fromTicket")} value={ticketId} onChange={(e) => fill(e.target.value)}>
          <option value="">{t("send.noTicket")}</option>
          {(tickets.data ?? []).map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
        </SelectField>
      ) : null}
      <TextField label={t("send.table")} value={table} onChange={(e) => setTable(e.target.value)} />
      <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} allowNotes />
      {problem ? <Notice kind="danger">{problem}</Notice> : null}
      <ErrorNotice error={action.error && action.error.code !== "kitchen.order_no_taken" ? action.error : null} t={kit} />
      <Inline><Button onClick={() => void send()} disabled={action.busy}>{t("send.button")}</Button></Inline>
    </Stack>
  );
}
