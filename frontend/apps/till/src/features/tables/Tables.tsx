import { Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, newId } from "@tillknowit/domain";
import { useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, TabGate, useAction, useKitT, useLoad, useT, who, type Caller, type Translate } from "../till-kit/kit";
import { LinesEditor } from "../till-kit/LinesEditor";
import type { TicketLineDto, TicketResponse } from "../till-kit/types";
import { messages, type TablesKey } from "./messages";
import { TicketPanel, type FloorTable } from "./TicketPanel";
import styles from "./Tables.module.css";

type Screen = { kind: "list" } | { kind: "new"; table: FloorTable | null } | { kind: "ticket"; ticket: TicketResponse };

export function Tables({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  if (!ctx.features.isOn("open_tickets")) return <Notice kind="warning">{t("needsTickets")}</Notice>;
  return <TabGate ctx={ctx}>{(caller) => <TablesBody ctx={ctx} caller={caller} />}</TabGate>;
}

function TablesBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const kit = useKitT(ctx.language);
  const [view, setView] = useState<"floor" | "tickets">("floor");
  const [screen, setScreen] = useState<Screen>({ kind: "list" });
  const [info, setInfo] = useState("");
  const plan = useLoad(() => ctx.api.get<FloorTable[]>(`/shops/${caller.shopId}/floor-plan`, who(caller)), [caller.shopId]);
  const tickets = useLoad(() => ctx.api.get<TicketResponse[]>(`/shops/${caller.shopId}/tickets`, who(caller)), [caller.shopId]);

  const tables = plan.data ?? [];
  const open = tickets.data ?? [];
  const reload = () => {
    plan.reload();
    tickets.reload();
  };

  if (screen.kind === "new") {
    return <NewTicket ctx={ctx} caller={caller} table={screen.table} t={t} back={() => setScreen({ kind: "list" })} created={(ticket) => { reload(); setScreen({ kind: "ticket", ticket }); }} />;
  }
  if (screen.kind === "ticket") {
    return (
      <TicketPanel
        key={screen.ticket.id}
        ctx={ctx}
        caller={caller}
        ticket={screen.ticket}
        tables={tables}
        others={open.filter((o) => o.id !== screen.ticket.id)}
        t={t}
        changed={reload}
        leave={(text) => { setInfo(text ?? ""); setScreen({ kind: "list" }); }}
      />
    );
  }

  const ticketsOn = (id: string) => open.filter((o) => o.tableId === id);
  const stateOf = (o: TicketResponse) => (o.lockedForYou ? t("state.locked") : o.heldByDeviceId === caller.deviceId ? t("state.mine") : t("state.free"));

  return (
    <Card title={t("title")}>
      <Stack>
        <Inline>
          <Button variant={view === "floor" ? "primary" : "secondary"} aria-pressed={view === "floor"} onClick={() => setView("floor")}>{t("view.floor")}</Button>
          <Button variant={view === "tickets" ? "primary" : "secondary"} aria-pressed={view === "tickets"} onClick={() => setView("tickets")}>{t("view.tickets")}</Button>
          <Button variant="secondary" onClick={() => setScreen({ kind: "new", table: null })}>{t("new.button")}</Button>
        </Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={tickets.error ?? plan.error} t={kit} />
        {plan.loading || tickets.loading ? <p>{kit("kit.loading")}</p> : null}

        {view === "floor" ? (
          tables.length === 0 && !plan.loading ? <p>{t("floor.empty")}</p> : (
            <ul className={styles.floor} aria-label={t("view.floor")}>
              {tables.map((tb) => {
                const there = ticketsOn(tb.id);
                const total = there.reduce((s, x) => s + x.totalCents, 0);
                const state = there.length === 0 ? t("floor.free") : there.length === 1 ? t("floor.busy", { total: formatRand(total) }) : t("floor.busyMany", { count: there.length, total: formatRand(total) });
                return (
                  <li key={tb.id}>
                    <button
                      type="button"
                      className={[styles.table, there.length > 0 ? styles.busy : ""].join(" ")}
                      aria-label={t("floor.open", { name: tb.name, state })}
                      onClick={() => setScreen(there[0] ? { kind: "ticket", ticket: there[0] } : { kind: "new", table: tb })}
                    >
                      <strong>{tb.name}</strong>
                      <span className={styles.muted}>{tb.area ? `${t("floor.area", { area: tb.area })}, ` : ""}{t("floor.seats", { count: tb.seats })}</span>
                      <span>{state}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )
        ) : (
          <Table
            empty={t("tickets.empty")}
            rows={open}
            rowKey={(o) => o.id}
            columns={[
              { header: t("tickets.colLabel"), cell: (o) => <Button variant="quiet" aria-label={t("tickets.open", { label: o.label })} onClick={() => setScreen({ kind: "ticket", ticket: o })}>{o.label}</Button> },
              { header: t("tickets.colTable"), cell: (o) => tables.find((x) => x.id === o.tableId)?.name ?? t("tickets.noTable") },
              { header: t("tickets.colTotal"), cell: (o) => formatRand(o.totalCents), align: "right" },
              { header: t("tickets.colState"), cell: stateOf },
            ]}
          />
        )}
      </Stack>
    </Card>
  );
}

function NewTicket({ ctx, caller, table, t, back, created }: {
  ctx: TillContext;
  caller: Caller;
  table: FloorTable | null;
  t: Translate<TablesKey>;
  back: () => void;
  created: (ticket: TicketResponse) => void;
}) {
  const kit = useKitT(ctx.language);
  const [label, setLabel] = useState(table?.name ?? "");
  const [lines, setLines] = useState<TicketLineDto[]>([]);
  const [problem, setProblem] = useState("");
  const [id] = useState(() => newId());
  const action = useAction();

  async function open() {
    if (label.trim() === "") return setProblem(t("new.labelRequired"));
    setProblem("");
    const r = await action.run(() => ctx.api.post<TicketResponse>(`/shops/${caller.shopId}/tickets`, { deviceId: caller.deviceId, staffId: caller.staffId, id, label: label.trim(), tableId: table?.id ?? null, lines }));
    if (r.ok) created(r.value);
  }

  return (
    <Card title={table ? t("new.titleTable", { name: table.name }) : t("new.title")}>
      <Stack>
        <TextField label={t("new.label")} hint={t("new.labelHint")} maxLength={40} value={label} onChange={(e) => setLabel(e.target.value)} error={problem || null} />
        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} allowNotes />
        <ErrorNotice error={action.error} t={kit} />
        <Inline>
          <Button onClick={() => void open()} disabled={action.busy}>{t("new.save")}</Button>
          <Button variant="quiet" onClick={back}>{t("new.cancel")}</Button>
        </Inline>
      </Stack>
    </Card>
  );
}
