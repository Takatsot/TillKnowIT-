import { formatDate, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { messages, type AccountingKey } from "./messages";

export interface Connection { id: string; provider: string; tenantName: string | null; connected: boolean; startsOn: string; connectedAt: string }
export interface MappingEntry { key: string; accountCode: string }
export interface Mappings { mappings: MappingEntry[]; missing: string[] }
export interface ExportRun { id: string; provider: string; day: string; status: string; attempts: number; error: string | null; externalReference: string | null; givenUp: boolean }

/** The nine kinds of account the daily journal posts to (Accounting.Domain AccountKeys). */
export const ACCOUNT_KEYS = ["cash", "card_clearing", "customer_credit", "other_receipts", "sales", "vat_output", "cost_of_sales", "stock", "creditors"] as const;

const isPlanError = (e: { code: string } | null) => e?.code === "plan.feature_not_included";

export function Accounting() {
  const [notice, setNotice] = useState("");
  return (
    <Stack>
      {notice && <Notice kind="success">{notice}</Notice>}
      <ConnectCard onNotice={setNotice} />
      <MappingsCard onNotice={setNotice} />
      <RunsCard onNotice={setNotice} />
    </Stack>
  );
}

function ConnectCard({ onNotice }: { onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [provider, setProvider] = useState("xero");
  const [code, setCode] = useState("");
  const [startsOn, setStartsOn] = useState("");
  const [connection, setConnection] = useState<Connection | null>(null);

  const connect = useAction(() => api.post<Connection>(`/shops/${shopId}/accounting/connections/${provider}`, { personId, authorisationCode: code.trim(), startsOn: startsOn === "" ? null : startsOn }));
  const disconnect = useAction(() => api.delete<unknown>(`/shops/${shopId}/accounting/connection`, { personId }));

  async function submit() {
    onNotice("");
    const r = await connect.run();
    if (r.ok) {
      setConnection(r.value);
      setCode("");
      onNotice(t("connected", { provider: r.value.provider === "sage" ? t("sage") : t("xero"), tenant: r.value.tenantName ? ` (${r.value.tenantName})` : "" }));
    }
  }

  async function doDisconnect() {
    const r = await disconnect.run();
    if (r.ok) {
      setConnection(null);
      onNotice(t("disconnected"));
    }
  }

  const error = connect.error ?? disconnect.error;
  return (
    <Card title={t("connectHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Notice kind="info">{t("oauthNote")}</Notice>
          <SelectField label={t("provider")} value={provider} onChange={(e) => setProvider(e.target.value)}>
            <option value="xero">{t("xero")}</option>
            <option value="sage">{t("sage")}</option>
          </SelectField>
          <TextField label={t("code")} value={code} onChange={(e) => setCode(e.target.value)} required autoComplete="off" />
          <TextField label={t("startsOn")} hint={t("startsOnHint")} type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
          {error && <Notice kind="danger">{isPlanError(error) ? t("planNote") : error.message}</Notice>}
          {connection && <p>{t("startsFrom", { date: formatDate(connection.startsOn) })}</p>}
          <Inline>
            <Button type="submit" disabled={connect.busy || code.trim() === ""}>{t("connect")}</Button>
            <Button variant="quiet" disabled={disconnect.busy} onClick={() => void doDisconnect()}>{t("disconnect")}</Button>
          </Inline>
          <p>{t("connectionUnknown")}</p>
        </Stack>
      </form>
    </Card>
  );
}

function MappingsCard({ onNotice }: { onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const load = useLoad(() => api.get<Mappings>(`/shops/${shopId}/accounting/mappings`, { personId }), [shopId, personId]);
  const [values, setValues] = useState<Record<string, string>>({});
  useEffect(() => {
    if (load.data) setValues(Object.fromEntries(load.data.mappings.map((m) => [m.key, m.accountCode])));
  }, [load.data]);

  const save = useAction(() => api.put<Mappings>(`/shops/${shopId}/accounting/mappings`, {
    personId, mappings: ACCOUNT_KEYS.filter((k) => (values[k] ?? "").trim() !== "").map((k) => ({ key: k, accountCode: (values[k] ?? "").trim() })),
  }));

  async function submit() {
    onNotice("");
    const r = await save.run();
    if (r.ok) {
      onNotice(t("saved"));
      load.reload();
    }
  }

  const missing = load.data?.missing ?? [];
  return (
    <Card title={t("mappingsHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("mappingsExplain")}</p>
          {load.error && <Notice kind="danger">{isPlanError(load.error) ? t("planNote") : load.error.message}</Notice>}
          {load.loading && !load.data && !load.error && <p>{t("loading")}</p>}
          {missing.length > 0 && <Notice kind="warning">{t("missing", { keys: missing.map((k) => keyLabel(t, k)).join(", ") })}</Notice>}
          {ACCOUNT_KEYS.map((k) => (
            <TextField key={k} label={t(`key_${k}`)} value={values[k] ?? ""} maxLength={40} onChange={(e) => setValues({ ...values, [k]: e.target.value })} />
          ))}
          {save.error && <Notice kind="danger">{isPlanError(save.error) ? t("planNote") : save.error.message}</Notice>}
          <div><Button type="submit" disabled={save.busy}>{t("save")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

function keyLabel(t: (k: AccountingKey) => string, key: string): string {
  const k = `key_${key}`;
  return k in messages.en ? t(k as AccountingKey) : key;
}

function RunsCard({ onNotice }: { onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const runs = useLoad(() => api.get<ExportRun[]>(`/shops/${shopId}/accounting/export-runs`, { personId }), [shopId, personId]);
  const retry = useAction((run: ExportRun) => api.post<ExportRun>(`/shops/${shopId}/accounting/export-runs/${run.id}/retry`, { personId }));
  const statusLabel = (s: string) => ({ Pending: t("statusPending"), Succeeded: t("statusSucceeded"), Failed: t("statusFailed") })[s] ?? s;

  async function doRetry(run: ExportRun) {
    onNotice("");
    const r = await retry.run(run);
    if (r.ok) {
      onNotice(t("retried", { day: formatDate(run.day) }));
      runs.reload();
    }
  }

  return (
    <Card title={t("runsHeading")}>
      <Stack>
        {runs.error && <Notice kind="danger">{isPlanError(runs.error) ? t("planNote") : runs.error.message}</Notice>}
        {retry.error && <Notice kind="danger">{retry.error.message}</Notice>}
        {runs.loading && !runs.data && !runs.error ? <p>{t("loading")}</p> : (
          <Table
            rows={runs.data ?? []}
            rowKey={(r) => r.id}
            empty={runs.error ? undefined : t("runsEmpty")}
            columns={[
              { header: t("colDay"), cell: (r) => formatDate(r.day) },
              { header: t("colProvider"), cell: (r) => (r.provider === "sage" ? t("sage") : r.provider === "xero" ? t("xero") : r.provider) },
              { header: t("colStatus"), cell: (r) => <Badge>{statusLabel(r.status)}</Badge> },
              { header: t("colAttempts"), align: "right", cell: (r) => r.attempts },
              { header: t("colReference"), cell: (r) => r.externalReference ?? "" },
              {
                header: t("colProblem"),
                cell: (r) => (
                  <>
                    {r.error ?? ""}
                    {r.givenUp && <div>{t("gaveUp")}</div>}
                    {r.status === "Failed" && <Button variant="secondary" disabled={retry.busy} onClick={() => void doRetry(r)}>{t("retry", { day: formatDate(r.day) })}</Button>}
                  </>
                ),
              },
            ]}
          />
        )}
      </Stack>
    </Card>
  );
}
