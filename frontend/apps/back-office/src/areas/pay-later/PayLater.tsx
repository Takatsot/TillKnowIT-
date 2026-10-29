import { formatDate, formatDateTime, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface Consent { id: string; partnerCode: string; months: number; grantedAt: string; withdrawnAt: string | null; active: boolean }
export interface CreditApplication {
  id: string; partnerCode: string; status: string; requestedCents: number; approvedCents: number; availableCents: number; termDays: number; reason: string | null; decidedAt: string | null;
}
export interface Draw { id: string; orderId: string; supplierName: string; principalCents: number; repayCents: number; repaidCents: number; dueOn: string; status: string }
export interface DataShare { at: string; partnerCode: string; months: number; totalSalesCents: number }
export interface CreditSummary { consents: Consent[]; applications: CreditApplication[]; draws: Draw[]; shared: DataShare[] }

export function PayLater() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const summary = useLoad(() => api.get<CreditSummary>(`/shops/${shopId}/credit`, { personId }), [shopId, personId]);
  const [notice, setNotice] = useState("");
  const data = summary.data;
  const planMissing = summary.error?.code === "plan.feature_not_included";

  function done(text: string) {
    setNotice(text);
    summary.reload();
  }

  return (
    <Stack>
      <Card title={t("explainHeading")}><p>{t("explain")}</p></Card>
      {summary.error && <Notice kind="danger">{planMissing ? t("planNote") : summary.error.message}</Notice>}
      {summary.loading && !data && !summary.error && <p>{t("loading")}</p>}
      {notice && <Notice kind="success">{notice}</Notice>}
      <ConsentCard consents={data?.consents ?? []} onDone={done} />
      <ApplyCard consents={(data?.consents ?? []).filter((c) => c.active)} applications={data?.applications ?? []} onDone={done} />
      <DrawCard applications={(data?.applications ?? []).filter((a) => a.status === "Approved")} draws={data?.draws ?? []} onDone={done} />
      <Card title={t("sharedHeading")}>
        <Table
          rows={data?.shared ?? []}
          rowKey={(s) => `${s.at}-${s.partnerCode}`}
          empty={t("sharedEmpty")}
          columns={[
            { header: t("colWhen"), cell: (s) => formatDateTime(s.at) },
            { header: t("colPartner"), cell: (s) => s.partnerCode },
            { header: t("colWhat"), cell: (s) => t("sharedWhat", { n: s.months, total: formatRand(s.totalSalesCents) }) },
          ]}
        />
      </Card>
    </Stack>
  );
}

function ConsentCard({ consents, onDone }: { consents: Consent[]; onDone: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [partner, setPartner] = useState("");
  const [months, setMonths] = useState("6");
  const n = Number(months);
  const monthsValid = months.trim() !== "" && Number.isInteger(n) && n >= 1 && n <= 24;
  const grant = useAction(() => api.post<Consent>(`/shops/${shopId}/credit/consents`, { personId, partnerCode: partner.trim(), months: n }));
  const withdraw = useAction((c: Consent) => api.delete<unknown>(`/shops/${shopId}/credit/consents/${c.id}`, { personId }));
  const error = grant.error ?? withdraw.error;

  async function submit() {
    const r = await grant.run();
    if (r.ok) {
      const name = r.value.partnerCode;
      setPartner("");
      onDone(t("granted", { partner: name }));
    }
  }

  async function doWithdraw(c: Consent) {
    const r = await withdraw.run(c);
    if (r.ok) onDone(t("withdrawn", { partner: c.partnerCode }));
  }

  return (
    <Card title={t("consentHeading")}>
      <Stack>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <TextField label={t("partner")} hint={t("partnerHint")} value={partner} onChange={(e) => setPartner(e.target.value)} required />
            <TextField label={t("months")} inputMode="numeric" value={months} error={monthsValid ? null : t("badMonths")} onChange={(e) => setMonths(e.target.value)} />
            <div><Button type="submit" disabled={grant.busy || partner.trim() === "" || !monthsValid}>{t("grant")}</Button></div>
          </Stack>
        </form>
        {error && <Notice kind="danger">{error.code === "plan.feature_not_included" ? t("planNote") : error.message}</Notice>}
        <h3>{t("consentsHeading")}</h3>
        <Table
          rows={consents}
          rowKey={(c) => c.id}
          empty={t("consentsEmpty")}
          columns={[
            { header: t("colPartner"), cell: (c) => c.partnerCode },
            { header: t("colMonths"), align: "right", cell: (c) => c.months },
            { header: t("colAgreed"), cell: (c) => formatDate(c.grantedAt) },
            { header: t("colState"), cell: (c) => <Badge>{c.active ? t("stateActive") : t("stateWithdrawn", { date: formatDate(c.withdrawnAt) })}</Badge> },
            { header: "", cell: (c) => c.active ? <Button variant="secondary" disabled={withdraw.busy} onClick={() => void doWithdraw(c)}>{t("withdraw", { partner: c.partnerCode })}</Button> : "" },
          ]}
        />
      </Stack>
    </Card>
  );
}

function ApplyCard({ consents, applications, onDone }: { consents: Consent[]; applications: CreditApplication[]; onDone: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [consentId, setConsentId] = useState("");
  const [amount, setAmount] = useState("");
  const cents = parseRandToCents(amount);
  const apply = useAction(() => api.post<CreditApplication>(`/shops/${shopId}/credit/applications`, { personId, id: crypto.randomUUID(), consentId, requestedCents: cents }));

  async function submit() {
    const r = await apply.run();
    if (r.ok) {
      setAmount("");
      onDone(t("applied"));
    }
  }

  const decision = (s: string) => ({ Submitted: t("decSubmitted"), Approved: t("decApproved"), Declined: t("decDeclined") })[s] ?? s;
  return (
    <Card title={t("applyHeading")}>
      <Stack>
        {consents.length === 0 ? <p>{t("applyNeedsConsent")}</p> : (
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <SelectField label={t("applyConsent")} value={consentId} onChange={(e) => setConsentId(e.target.value)}>
                <option value="">{t("applyChoose")}</option>
                {consents.map((c) => <option key={c.id} value={c.id}>{c.partnerCode}</option>)}
              </SelectField>
              <TextField label={t("applyAmount")} inputMode="decimal" value={amount} error={amount !== "" && (cents === null || cents <= 0) ? t("applyBadAmount") : null} onChange={(e) => setAmount(e.target.value)} />
              <div><Button type="submit" disabled={apply.busy || consentId === "" || cents === null || cents <= 0}>{t("apply")}</Button></div>
            </Stack>
          </form>
        )}
        {apply.error && <Notice kind="danger">{apply.error.code === "plan.feature_not_included" ? t("planNote") : apply.error.message}</Notice>}
        <h3>{t("applicationsHeading")}</h3>
        <Table
          rows={applications}
          rowKey={(a) => a.id}
          empty={t("applicationsEmpty")}
          columns={[
            { header: t("colPartner"), cell: (a) => a.partnerCode },
            { header: t("colStatus"), cell: (a) => <Badge>{decision(a.status)}</Badge> },
            { header: t("colRequested"), align: "right", cell: (a) => formatRand(a.requestedCents) },
            { header: t("colLimit"), align: "right", cell: (a) => (a.status === "Approved" ? formatRand(a.approvedCents) : "") },
            { header: t("colAvailable"), align: "right", cell: (a) => (a.status === "Approved" ? formatRand(a.availableCents) : "") },
            { header: t("colTerm"), cell: (a) => (a.status === "Approved" ? t("termDays", { n: a.termDays }) : "") },
            { header: t("colReason"), cell: (a) => a.reason ?? "" },
          ]}
        />
      </Stack>
    </Card>
  );
}

function DrawCard({ applications, draws, onDone }: { applications: CreditApplication[]; draws: Draw[]; onDone: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [applicationId, setApplicationId] = useState("");
  const [orderId, setOrderId] = useState("");
  const draw = useAction(() => api.post<Draw>(`/shops/${shopId}/credit/draws`, { personId, applicationId, orderId: orderId.trim() }));

  async function submit() {
    const r = await draw.run();
    if (r.ok) {
      setOrderId("");
      onDone(t("drawn", { date: formatDate(r.value.dueOn) }));
    }
  }

  return (
    <Card title={t("drawHeading")}>
      <Stack>
        {applications.length === 0 ? <p>{t("noApproved")}</p> : (
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <SelectField label={t("drawApplication")} value={applicationId} onChange={(e) => setApplicationId(e.target.value)}>
                <option value="">{t("applyChoose")}</option>
                {applications.map((a) => <option key={a.id} value={a.id}>{t("drawApplicationOption", { partner: a.partnerCode, available: formatRand(a.availableCents) })}</option>)}
              </SelectField>
              <TextField label={t("drawOrder")} hint={t("drawOrderHint")} value={orderId} onChange={(e) => setOrderId(e.target.value)} />
              <div><Button type="submit" disabled={draw.busy || applicationId === "" || orderId.trim() === ""}>{t("draw")}</Button></div>
            </Stack>
          </form>
        )}
        {draw.error && <Notice kind="danger">{draw.error.code === "plan.feature_not_included" ? t("planNote") : draw.error.message}</Notice>}
        <h3>{t("drawsHeading")}</h3>
        <p>{t("repayNote")}</p>
        <Table
          rows={draws}
          rowKey={(d) => d.id}
          empty={t("drawsEmpty")}
          columns={[
            { header: t("colSupplier"), cell: (d) => d.supplierName },
            { header: t("colPrincipal"), align: "right", cell: (d) => formatRand(d.principalCents) },
            { header: t("colRepay"), align: "right", cell: (d) => formatRand(d.repayCents) },
            { header: t("colRepaid"), align: "right", cell: (d) => formatRand(d.repaidCents) },
            { header: t("colDue"), cell: (d) => formatDate(d.dueOn) },
            { header: t("colStatus"), cell: (d) => <Badge>{d.status === "Repaid" ? t("drawRepaid") : t("drawOpen")}</Badge> },
          ]}
        />
      </Stack>
    </Card>
  );
}
