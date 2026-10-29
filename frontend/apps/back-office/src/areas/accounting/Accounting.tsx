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

/** Where the package's sign-in page sent the browser back to: /?accounting=connected, or ?accounting=failed&reason=... Read once, then removed from the address. */
function returnedFromSignIn(search: string): { outcome: "connected" | "failed"; reason: string } | null {
  const params = new URLSearchParams(search);
  const outcome = params.get("accounting");
  return outcome === "connected" || outcome === "failed" ? { outcome, reason: params.get("reason") ?? "" } : null;
}

export function Accounting({ goTo = (url: string) => window.location.assign(url), search = typeof window === "undefined" ? "" : window.location.search }: { goTo?: (url: string) => void; search?: string }) {
  const t = useMessages(messages);
  const [notice, setNotice] = useState("");
  const [failure, setFailure] = useState("");
  useEffect(() => {
    const back = returnedFromSignIn(search);
    if (!back) return;
    if (back.outcome === "connected") setNotice(t("signInDone"));
    else setFailure(back.reason || t("signInFailed"));
    try { window.history.replaceState(null, "", window.location.pathname); } catch { /* the address stays as it is */ }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);
  return (
    <Stack>
      {notice && <Notice kind="success">{notice}</Notice>}
      {failure && <Notice kind="danger">{failure}</Notice>}
      <ConnectCard onNotice={setNotice} goTo={goTo} />
      <MappingsCard onNotice={setNotice} />
      <RunsCard onNotice={setNotice} />
    </Stack>
  );
}

function ConnectCard({ onNotice, goTo }: { onNotice: (s: string) => void; goTo: (url: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [provider, setProvider] = useState("xero");
  const [code, setCode] = useState("");
  const [startsOn, setStartsOn] = useState("");
  const existing = useLoad(() => api.get<Connection>(`/shops/${shopId}/accounting/connection`, { personId }), [shopId, personId]);
  const [override, setOverride] = useState<{ value: Connection | null } | null>(null);
  const connection = override ? override.value : existing.data;
  const setConnection = (c: Connection | null) => setOverride({ value: c });
  // accounting.not_connected is the normal answer for a shop with no package yet, not a failure.
  const loadError = existing.error && existing.error.code !== "accounting.not_connected" ? existing.error : null;

  const connect = useAction(() => api.post<Connection>(`/shops/${shopId}/accounting/connections/${provider}`, { personId, authorisationCode: code.trim(), startsOn: startsOn === "" ? null : startsOn }));
  const disconnect = useAction(() => api.delete<unknown>(`/shops/${shopId}/accounting/connection`, { personId }));
  const signIn = useAction(() => api.get<{ url: string }>(`/shops/${shopId}/accounting/connections/${provider}/authorise`, { personId, ...(startsOn === "" ? {} : { startsOn }) }));

  /** The package's own sign-in page: the server makes the address (with a signed state) and the browser goes there; the package sends it back to the callback, which connects and returns here. */
  async function startSignIn() {
    onNotice("");
    const r = await signIn.run();
    if (r.ok) goTo(r.value.url);
  }

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

  const error = connect.error ?? disconnect.error ?? signIn.error ?? loadError;
  const providerName = (p: string) => (p === "sage" ? t("sage") : t("xero"));
  return (
    <Card title={t("connectHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <SelectField label={t("provider")} value={provider} onChange={(e) => setProvider(e.target.value)}>
            <option value="xero">{t("xero")}</option>
            <option value="sage">{t("sage")}</option>
          </SelectField>
          <TextField label={t("startsOn")} hint={t("startsOnHint")} type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
          <Inline>
            <Button type="button" disabled={signIn.busy} onClick={() => void startSignIn()}>{t("signInWith", { provider: providerName(provider) })}</Button>
          </Inline>
          <p>{t("oauthNote")}</p>
          <TextField label={t("code")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" />
          {error && <Notice kind="danger">{isPlanError(error) ? t("planNote") : error.message}</Notice>}
          {existing.loading && !override && <p>{t("connectionLoading")}</p>}
          {connection && <Notice kind="info">{t("currentConnection", { provider: providerName(connection.provider), tenant: connection.tenantName ? ` (${connection.tenantName})` : "" })}</Notice>}
          {connection && <p>{t("startsFrom", { date: formatDate(connection.startsOn) })}</p>}
          <Inline>
            <Button type="submit" disabled={connect.busy || code.trim() === ""}>{connection ? t("reconnect") : t("connect")}</Button>
            {connection && <Button variant="quiet" disabled={disconnect.busy} onClick={() => void doDisconnect()}>{t("disconnect")}</Button>}
          </Inline>
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
