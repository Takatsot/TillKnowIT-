import { formatDate, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type InsightsKey } from "./messages";

export interface OptIn { shopId: string; optedIn: boolean; changedAt: string | null; explanation: string }
export interface Grant { id: string; clientId: string; clientName: string; scopes: string[]; approvedAt: string; active: boolean }

/** The scopes partners can be given (Platform.Domain ApiScopes). */
export const SCOPES = ["sales.daily", "sales.monthly"] as const;

export function InsightsAndPartners() {
  const [notice, setNotice] = useState("");
  return (
    <Stack>
      {notice && <Notice kind="success">{notice}</Notice>}
      <OptInCard onNotice={setNotice} />
      <PartnersCard onNotice={setNotice} />
    </Stack>
  );
}

function OptInCard({ onNotice }: { onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const load = useLoad(() => api.get<OptIn>(`/shops/${shopId}/insights-opt-in`, { personId }), [shopId, personId]);
  const save = useAction((optedIn: boolean) => api.put<OptIn>(`/shops/${shopId}/insights-opt-in`, { personId, optedIn }));

  async function toggle(optedIn: boolean) {
    onNotice("");
    const r = await save.run(optedIn);
    if (r.ok) {
      onNotice(t("optInSaved"));
      load.reload();
    }
  }

  const data = load.data;
  const error = load.error ?? save.error;
  return (
    <Card title={t("insightsHeading")}>
      <Stack>
        {load.loading && !data && !load.error && <p>{t("loading")}</p>}
        {error && <Notice kind="danger">{error.code === "plan.feature_not_included" ? t("planNote") : error.message}</Notice>}
        {data && (
          <>
            <p>{data.explanation}</p>
            <CheckField label={t("optIn")} checked={data.optedIn} disabled={save.busy} onChange={(e) => void toggle(e.target.checked)} />
            <p>{t("optInState", { state: data.optedIn ? t("stateIn") : t("stateOut") })}{data.changedAt ? ` ${t("changedOn", { date: formatDate(data.changedAt) })}` : ""}</p>
          </>
        )}
      </Stack>
    </Card>
  );
}

function PartnersCard({ onNotice }: { onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const list = useLoad(() => api.get<Grant[]>(`/shops/${shopId}/partner-access`, { personId }), [shopId, personId]);
  const [clientId, setClientId] = useState("");
  const [chosen, setChosen] = useState<string[]>([]);
  const grant = useAction(() => api.post<Grant>(`/shops/${shopId}/partner-access`, { personId, clientId: clientId.trim(), scopes: chosen }));
  const withdraw = useAction((g: Grant) => api.delete<unknown>(`/shops/${shopId}/partner-access/${g.id}`, { personId }));

  async function submit() {
    onNotice("");
    const r = await grant.run();
    if (r.ok) {
      onNotice(t("granted", { partner: r.value.clientName }));
      setClientId("");
      setChosen([]);
      list.reload();
    }
  }

  async function doWithdraw(g: Grant) {
    onNotice("");
    const r = await withdraw.run(g);
    if (r.ok) {
      onNotice(t("withdrawn", { partner: g.clientName }));
      list.reload();
    }
  }

  const scopeLabel = (s: string) => {
    const key = `scope_${s.replace(".", "_")}`;
    return key in messages.en ? t(key as InsightsKey) : s;
  };
  const error = grant.error ?? withdraw.error;

  return (
    <Card title={t("partnersHeading")}>
      <Stack>
        <p>{t("partnersExplain")}</p>
        <h3>{t("grantHeading")}</h3>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <TextField label={t("clientId")} hint={t("clientHint")} value={clientId} onChange={(e) => setClientId(e.target.value)} required />
            <fieldset>
              <legend>{t("scopes")}</legend>
              {SCOPES.map((s) => (
                <CheckField key={s} label={scopeLabel(s)} checked={chosen.includes(s)} onChange={(e) => setChosen(e.target.checked ? [...chosen, s] : chosen.filter((x) => x !== s))} />
              ))}
            </fieldset>
            {error && <Notice kind="danger">{error.code === "plan.feature_not_included" ? t("planNote") : error.message}</Notice>}
            <div><Button type="submit" disabled={grant.busy || clientId.trim() === "" || chosen.length === 0}>{t("grant")}</Button></div>
          </Stack>
        </form>
        <h3>{t("grantsHeading")}</h3>
        {list.error && <Notice kind="danger">{list.error.code === "plan.feature_not_included" ? t("planNote") : list.error.message}</Notice>}
        {list.loading && !list.data && !list.error ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(g) => g.id}
            empty={list.error ? undefined : t("grantsEmpty")}
            columns={[
              { header: t("colPartner"), cell: (g) => g.clientName },
              { header: t("colScopes"), cell: (g) => g.scopes.map(scopeLabel).join(", ") },
              { header: t("colApproved"), cell: (g) => formatDate(g.approvedAt) },
              { header: t("colState"), cell: (g) => <Badge>{g.active ? t("stateActive") : t("stateWithdrawn")}</Badge> },
              { header: "", cell: (g) => g.active ? <Button variant="secondary" disabled={withdraw.busy} onClick={() => void doWithdraw(g)}>{t("withdraw", { partner: g.clientName })}</Button> : "" },
            ]}
          />
        )}
      </Stack>
    </Card>
  );
}
