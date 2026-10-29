import { formatDate, formatRand, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField, TextAreaField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type SchoolsKey } from "./messages";
import type { Payout, Reconciliation, RunPayouts, ShopSales } from "./types";

function PayoutTable({ rows }: { rows: Payout[] }) {
  const t = useMessages(messages);
  return (
    <Table
      rows={rows}
      rowKey={(p) => p.id}
      columns={[
        { header: t("colShop"), cell: (p) => p.shopId },
        { header: t("colDay"), cell: (p) => formatDate(p.day) },
        { header: t("colAmount"), cell: (p) => formatRand(p.amountCents), align: "right" },
        { header: t("colStatus"), cell: (p) => <Badge>{p.status}</Badge> },
        { header: t("colError"), cell: (p) => p.error ?? "" },
      ]}
    />
  );
}

export function PayoutsTab({ defaultShopId }: { defaultShopId: string }) {
  return (
    <Stack>
      <Run />
      <ShopSalesCard defaultShopId={defaultShopId} />
    </Stack>
  );
}

function Run() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [day, setDay] = useState("");
  const [result, setResult] = useState<RunPayouts | null>(null);
  const run = useAction(() => api.post<RunPayouts>("/admin/wallet-payouts/run", undefined, { callerPersonId: personId, day }));

  async function submit() {
    setResult(null);
    const r = await run.run();
    if (r.ok) setResult(r.value);
  }

  return (
    <Card title={t("runHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("runIntro")}</p>
          <Inline>
            <TextField label={t("day")} type="date" value={day} onChange={(e) => setDay(e.target.value)} />
            <Button type="submit" disabled={run.busy}>{run.busy ? t("busy") : t("runPayouts")}</Button>
          </Inline>
          {run.error && <Notice kind="danger">{run.error.message}</Notice>}
          {result?.blocked && <Notice kind="warning">{t("blocked", { reason: result.blockedReason ?? "" })}</Notice>}
          {result && !result.blocked && result.payouts.length === 0 && <Notice kind="info">{t("nonePaid")}</Notice>}
          {result && result.payouts.length > 0 && <PayoutTable rows={result.payouts} />}
        </Stack>
      </form>
    </Card>
  );
}

function ShopSalesCard({ defaultShopId }: { defaultShopId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [shopId, setShopId] = useState(defaultShopId);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [summary, setSummary] = useState<ShopSales | null>(null);
  const load = useAction(() => api.get<ShopSales>(`/shops/${shopId.trim()}/wallet-payouts`, { callerPersonId: personId, from, to }));

  async function submit() {
    setSummary(null);
    const r = await load.run();
    if (r.ok) setSummary(r.value);
  }

  return (
    <Card title={t("salesHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("shopId")} value={shopId} onChange={(e) => setShopId(e.target.value)} required />
            <TextField label={t("from")} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <TextField label={t("to")} type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            <Button type="submit" variant="secondary" disabled={load.busy || shopId.trim() === ""}>{t("showSales")}</Button>
          </Inline>
          {load.error && <Notice kind="danger">{load.error.message}</Notice>}
          {summary && (
            <Stack>
              <Table
                rows={[summary]}
                rowKey={() => "summary"}
                columns={[
                  { header: t("sales"), cell: (s) => formatRand(s.salesCents), align: "right" },
                  { header: t("fees"), cell: (s) => formatRand(s.feesCents), align: "right" },
                  { header: t("paidOut"), cell: (s) => formatRand(s.paidOutCents), align: "right" },
                  { header: t("owed"), cell: (s) => formatRand(s.owedToShopCents), align: "right" },
                ]}
              />
              {summary.payouts.length === 0 ? <p>{t("noPayouts")}</p> : <PayoutTable rows={summary.payouts} />}
            </Stack>
          )}
        </Stack>
      </form>
    </Card>
  );
}

export function ReconciliationTab() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [day, setDay] = useState(() => formatDate(new Date()));
  const [runs, setRuns] = useState<Reconciliation[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [done, setDone] = useState("");
  const run = useAction(() => api.post<Reconciliation>("/admin/wallet-reconciliations", undefined, { day, callerPersonId: personId }));
  const resolve = useAction((id: string) => api.post<Reconciliation>(`/admin/wallet-reconciliations/${id}/resolve`, { callerPersonId: personId, note: note.trim() }));

  async function submit() {
    setDone("");
    const r = await run.run();
    if (r.ok) setRuns((rs) => [r.value, ...rs.filter((x) => x.id !== r.value.id)]);
  }

  async function finish(id: string) {
    const r = await resolve.run(id);
    if (r.ok) {
      setRuns((rs) => rs.map((x) => (x.id === id ? r.value : x)));
      setDone(t("resolveDone", { day: r.value.day }));
      setOpen(null);
      setNote("");
    }
  }

  const statusLabel = (s: string) => t(`${s}Status` as SchoolsKey);

  return (
    <Card title={t("reconHeading")}>
      <Stack>
        <p>{t("reconIntro")}</p>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Inline>
            <TextField label={t("day")} type="date" value={day} onChange={(e) => setDay(e.target.value)} required />
            <Button type="submit" disabled={run.busy || day === ""}>{run.busy ? t("busy") : t("reconRun")}</Button>
          </Inline>
        </form>
        {run.error && <Notice kind="danger">{run.error.message}</Notice>}
        {done && <Notice kind="success">{done}</Notice>}
        {runs.map((r) => (
          <Card key={r.id} title={t("reconResult", { day: r.day })}>
            <Stack>
              <Badge>{statusLabel(r.status)}</Badge>
              <Table
                rows={[r]}
                rowKey={(x) => x.id}
                columns={[
                  { header: t("partnerTotal"), cell: (x) => formatRand(x.partnerTotalCents), align: "right" },
                  { header: t("ledgerTotal"), cell: (x) => formatRand(x.ledgerTotalCents), align: "right" },
                  { header: t("difference"), cell: (x) => formatRand(x.differenceCents), align: "right" },
                ]}
              />
              {r.note && <p>{r.note}</p>}
              {r.status === "mismatch" && (open === r.id ? (
                <form onSubmit={(e) => { e.preventDefault(); void finish(r.id); }}>
                  <Stack>
                    <TextAreaField label={t("note")} hint={t("noteHint")} value={note} onChange={(e) => setNote(e.target.value)} />
                    {resolve.error && <Notice kind="danger">{resolve.error.message}</Notice>}
                    <Button type="submit" disabled={resolve.busy || note.trim().length < 5}>{t("resolve")}</Button>
                  </Stack>
                </form>
              ) : <Button variant="secondary" onClick={() => { resolve.clear(); setOpen(r.id); }}>{t("resolve")}</Button>)}
            </Stack>
          </Card>
        ))}
      </Stack>
    </Card>
  );
}
