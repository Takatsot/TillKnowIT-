import { formatDate, formatRand, useAction, useApi, useLoad, useMessages, useSession, type Loaded } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type BillingKey } from "./messages";

export interface Plan { id: string; code: string; audience: string; name: string; priceCents: number; isActive: boolean; features: { feature: string; limit: number | null }[] }
export interface Subscription {
  id: string; organisationId: string; planCode: string; status: string; periodStart: string; periodEnd: string;
  pendingPlanCode: string | null; cancelAtPeriodEnd: boolean; invoiceId: string | null; chargedCents: number;
}
export interface InvoiceLine { description: string; metric: string | null; quantity: number; unitCents: number; amountCents: number }
export interface Invoice {
  id: string; number: string; status: string; subtotalCents: number; vatPercent: number; vatCents: number; totalCents: number; dueAt: string; paidAt: string | null; lines: InvoiceLine[];
}

const AUDIENCE = "shop";

export function Billing() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const plans = useLoad(() => api.get<Plan[]>("/plans", { audience: AUDIENCE }), []);
  const sub = useLoad(() => api.get<Subscription>(`/organisations/${shopId}/subscription`, { personId, audience: AUDIENCE }), [shopId, personId]);
  const invoices = useLoad(() => api.get<Invoice[]>(`/organisations/${shopId}/invoices`, { personId, audience: AUDIENCE }), [shopId, personId]);
  const [message, setMessage] = useState("");

  const subscription = sub.data;
  const noSubscription = sub.error?.code === "subscription.not_found";
  const planOf = (code: string) => plans.data?.find((p) => p.code === code);
  const running = subscription && subscription.status !== "Cancelled" ? subscription : null;
  const current = running ? planOf(running.planCode) : undefined;

  const subscribe = useAction((planCode: string) => api.post<Subscription>("/subscriptions", { personId, organisationId: shopId, planCode }));
  const change = useAction((body: { planCode: string | null; cancel: boolean }) => api.patch<Subscription>(`/organisations/${shopId}/subscription`, { personId, ...body }));

  function reloadAll() {
    sub.reload();
    invoices.reload();
  }

  async function choose(plan: Plan) {
    setMessage("");
    const r = running ? await change.run({ planCode: plan.code, cancel: false }) : await subscribe.run(plan.code);
    if (r.ok) {
      setMessage(running ? t("changed") : t("subscribed", { plan: plan.name }));
      reloadAll();
    }
  }

  async function cancel() {
    setMessage("");
    const r = await change.run({ planCode: null, cancel: true });
    if (r.ok) {
      setMessage(t("cancelled"));
      reloadAll();
    }
  }

  const statusLabel = (s: string) => ({ Active: t("statusActive"), PastDue: t("statusPastDue"), Limited: t("statusLimited"), Cancelled: t("statusCancelled") })[s] ?? s;
  const actionError = subscribe.error ?? change.error;
  const activePlans = (plans.data ?? []).filter((p) => p.isActive);

  return (
    <Stack>
      {subscription?.status === "PastDue" && <Notice kind="warning">{t("pastDue")} {t("tillNote")}</Notice>}
      {subscription?.status === "Limited" && <Notice kind="danger">{t("limited")} {t("tillNote")}</Notice>}
      {message && <Notice kind="success">{message}</Notice>}
      {actionError && <Notice kind="danger">{actionError.message}</Notice>}

      <Card title={t("currentHeading")}>
        <Stack>
          {sub.loading && !sub.data && !sub.error && <p>{t("loading")}</p>}
          {sub.error && !noSubscription && <Notice kind="danger">{sub.error.message}</Notice>}
          {noSubscription && <p>{t("noSubscription")}</p>}
          {subscription && (
            <>
              <p><strong>{t("planName")}:</strong> {current?.name ?? subscription.planCode} <Badge>{statusLabel(subscription.status)}</Badge></p>
              {running && <p><strong>{t("periodEnds")}:</strong> {formatDate(running.periodEnd)}</p>}
              {subscription.cancelAtPeriodEnd && <Notice kind="info">{t("cancelling", { date: formatDate(subscription.periodEnd) })}</Notice>}
              {subscription.pendingPlanCode && <Notice kind="info">{t("pending", { plan: planOf(subscription.pendingPlanCode)?.name ?? subscription.pendingPlanCode, date: formatDate(subscription.periodEnd) })}</Notice>}
              {running && !subscription.cancelAtPeriodEnd && (
                <div><Button variant="quiet" disabled={change.busy} onClick={() => void cancel()}>{t("cancelPlan")}</Button></div>
              )}
            </>
          )}
          <p>{t("tillNote")}</p>
        </Stack>
      </Card>

      <Card title={t("plansHeading")}>
        {plans.error && <Notice kind="danger">{plans.error.message}</Notice>}
        {plans.loading && !plans.data ? <p>{t("loading")}</p> : activePlans.length === 0 ? <p>{t("plansEmpty")}</p> : (
          <ul>
            {activePlans.map((p) => {
              const isCurrent = running?.planCode === p.code;
              const verb: BillingKey = !current ? "subscribe" : p.priceCents > current.priceCents ? "upgrade" : "downgrade";
              return (
                <li key={p.code}>
                  <Stack>
                    <strong>{p.name}</strong>
                    <span>{p.priceCents === 0 ? t("free") : t("perMonth", { price: formatRand(p.priceCents) })}</span>
                    <ul>
                      {p.features.map((f) => (
                        <li key={f.feature}>{featureName(t, f.feature)}: {f.limit === null ? t("unlimited") : t("limit", { n: f.limit })}</li>
                      ))}
                    </ul>
                    {isCurrent ? <Badge>{t("current")}</Badge> : (
                      <div><Button disabled={subscribe.busy || change.busy} onClick={() => void choose(p)}>{t(verb, { plan: p.name })}</Button></div>
                    )}
                  </Stack>
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      <InvoiceList invoices={invoices} onPaid={(n) => { setMessage(t("paid", { number: n })); reloadAll(); }} />
    </Stack>
  );
}

function featureName(t: (k: BillingKey) => string, feature: string): string {
  const key = `feature_${feature}`;
  return key in messages.en ? t(key as BillingKey) : feature;
}

function InvoiceList({ invoices, onPaid }: { invoices: Loaded<Invoice[]>; onPaid: (number: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [open, setOpen] = useState<string | null>(null);
  const pay = useAction((invoice: Invoice) => api.post<Invoice>(`/invoices/${invoice.id}/pay`, undefined, { personId, audience: AUDIENCE }));
  const statusLabel = (s: string) => ({ Open: t("invOpen"), Paid: t("invPaid"), Failed: t("invFailed"), Void: t("invVoid") })[s] ?? s;
  const shown = invoices.data?.find((i) => i.id === open);

  async function doPay(i: Invoice) {
    const r = await pay.run(i);
    if (r.ok) onPaid(i.number);
  }

  return (
    <Card title={t("invoicesHeading")}>
      <Stack>
        {invoices.error && <Notice kind="danger">{invoices.error.message}</Notice>}
        {pay.error && <Notice kind="danger">{pay.error.message}</Notice>}
        {invoices.loading && !invoices.data ? <p>{t("loading")}</p> : (
          <Table
            rows={invoices.data ?? []}
            rowKey={(i) => i.id}
            empty={t("invoicesEmpty")}
            columns={[
              { header: t("colNumber"), cell: (i) => i.number },
              { header: t("colStatus"), cell: (i) => <Badge>{statusLabel(i.status)}</Badge> },
              { header: t("colTotal"), align: "right", cell: (i) => formatRand(i.totalCents) },
              { header: t("colDue"), cell: (i) => formatDate(i.dueAt) },
              { header: t("colPaid"), cell: (i) => formatDate(i.paidAt) },
              {
                header: t("colActions"),
                cell: (i) => (
                  <Inline>
                    <Button variant="secondary" onClick={() => setOpen(open === i.id ? null : i.id)}>{t(open === i.id ? "hideLines" : "viewLines", { number: i.number })}</Button>
                    {i.status === "Failed" && <Button disabled={pay.busy} onClick={() => void doPay(i)}>{t("pay", { number: i.number })}</Button>}
                  </Inline>
                ),
              },
            ]}
          />
        )}
        {shown && (
          <Stack>
            <h3>{t("linesHeading", { number: shown.number })}</h3>
            <Table
              rows={shown.lines}
              rowKey={(l) => `${l.description}-${l.metric ?? ""}-${l.unitCents}`}
              columns={[
                { header: t("lineDescription"), cell: (l) => l.description },
                { header: t("lineQuantity"), align: "right", cell: (l) => l.quantity },
                { header: t("lineUnit"), align: "right", cell: (l) => formatRand(l.unitCents) },
                { header: t("lineAmount"), align: "right", cell: (l) => formatRand(l.amountCents) },
              ]}
            />
            <p>{t("subtotal")}: {formatRand(shown.subtotalCents)}</p>
            <p>{t("vat", { percent: shown.vatPercent })}: {formatRand(shown.vatCents)}</p>
            <p><strong>{t("total")}: {formatRand(shown.totalCents)}</strong></p>
          </Stack>
        )}
      </Stack>
    </Card>
  );
}
