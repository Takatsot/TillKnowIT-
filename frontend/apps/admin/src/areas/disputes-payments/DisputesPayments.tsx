import { formatDate, formatDateTime, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface Dispute {
  id: string;
  orderId: string;
  shopId: string;
  supplierId: string;
  raisedBy: string;
  reason: string;
  status: string;
  outcome: string | null;
  note: string | null;
  raisedAt: string;
  resolvedAt: string | null;
}

export interface Reconciliation {
  day: string;
  matched: number;
  mismatches: { partnerRef: string; kind: string; detail: string }[];
}

/** The outcomes the server accepts (Payments.Application Disputes.cs). */
export const outcomes = ["pay_supplier", "refund_shop"] as const;

export function DisputesPayments() {
  return (
    <Stack>
      <Disputes />
      <Reconcile />
    </Stack>
  );
}

function Disputes() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Dispute[]>("/admin/disputes", { callerPersonId: personId }), [personId]);
  const [open, setOpen] = useState<Dispute | null>(null);
  const [outcome, setOutcome] = useState<string>(outcomes[0]);
  const [note, setNote] = useState("");
  const [done, setDone] = useState("");
  const resolve = useAction(() => api.post<Dispute>(`/admin/disputes/${open!.id}/resolve`, { callerPersonId: personId, outcome, note: note.trim() === "" ? null : note.trim() }));

  async function submit() {
    const order = open!.orderId;
    const r = await resolve.run();
    if (r.ok) {
      setDone(t("resolved", { order }));
      setOpen(null);
      setNote("");
      list.reload();
    }
  }

  return (
    <>
      <Card title={t("disputesHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {done && <Notice kind="success">{done}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(d) => d.id}
            empty={t("empty")}
            columns={[
              { header: t("colRaised"), cell: (d) => formatDateTime(d.raisedAt) },
              { header: t("colBy"), cell: (d) => d.raisedBy },
              { header: t("colOrder"), cell: (d) => d.orderId },
              { header: t("colReason"), cell: (d) => d.reason },
              { header: t("colStatus"), cell: (d) => <Badge>{d.status}</Badge> },
              {
                header: t("colActions"),
                cell: (d) => <Button variant="secondary" aria-label={`${t("resolve")} ${d.orderId}`} onClick={() => { resolve.clear(); setDone(""); setOpen(d); }}>{t("resolve")}</Button>,
              },
            ]}
          />
        )}
      </Card>
      {open && (
        <Card title={t("resolving", { order: open.orderId })}>
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <p>{open.reason}</p>
              <SelectField label={t("outcome")} hint={t("outcomeHint")} value={outcome} onChange={(e) => setOutcome(e.target.value)}>
                {outcomes.map((o) => <option key={o} value={o}>{t(o)}</option>)}
              </SelectField>
              <TextField label={t("note")} value={note} onChange={(e) => setNote(e.target.value)} />
              {resolve.error && <Notice kind="danger">{resolve.error.message}</Notice>}
              <Inline>
                <Button type="submit" disabled={resolve.busy}>{t("confirm")}</Button>
                <Button variant="quiet" onClick={() => setOpen(null)}>{t("close")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}
    </>
  );
}

function Reconcile() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [day, setDay] = useState(() => formatDate(new Date()));
  const [report, setReport] = useState<Reconciliation | null>(null);
  const run = useAction(() => api.post<Reconciliation>("/admin/payments/reconcile", undefined, { day, callerPersonId: personId }));

  async function submit() {
    setReport(null);
    const r = await run.run();
    if (r.ok) setReport(r.value);
  }

  return (
    <Card title={t("reconcileHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("reconcileIntro")}</p>
          <Inline>
            <TextField label={t("day")} type="date" value={day} onChange={(e) => setDay(e.target.value)} required />
            <Button type="submit" disabled={run.busy || day === ""}>{run.busy ? t("running") : t("run")}</Button>
          </Inline>
          {run.error && <Notice kind="danger">{run.error.message}</Notice>}
          {report && (
            <Stack>
              <h3>{t("report", { day: report.day })}</h3>
              <Notice kind={report.mismatches.length === 0 ? "success" : "warning"}>
                {t("matched", { count: report.matched })} {report.mismatches.length === 0 ? t("noMismatches") : t("mismatches", { count: report.mismatches.length })}
              </Notice>
              {report.mismatches.length > 0 && (
                <Table
                  rows={report.mismatches}
                  rowKey={(m) => `${m.partnerRef}|${m.kind}`}
                  columns={[
                    { header: t("colRef"), cell: (m) => m.partnerRef },
                    { header: t("colKind"), cell: (m) => m.kind },
                    { header: t("colDetail"), cell: (m) => m.detail },
                  ]}
                />
              )}
            </Stack>
          )}
        </Stack>
      </form>
    </Card>
  );
}
