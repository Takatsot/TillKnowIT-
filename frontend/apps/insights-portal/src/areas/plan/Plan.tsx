import { formatDate, formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, Stack, Table } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type PlanKey } from "./messages";

interface PlanDto { id: string; code: string; name: string; priceCents: number; features: { feature: string; limit: number | null }[] }
interface Subscription { planCode: string; status: string; periodStart: string; periodEnd: string; pendingPlanCode: string | null; cancelAtPeriodEnd: boolean }

export function Plan() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, supplierOrganisationId } = useSession();
  const plans = useLoad(() => api.get<PlanDto[]>("/plans", { audience: "supplier" }), []);
  const sub = useLoad(() => api.get<Subscription>(`/organisations/${supplierOrganisationId}/subscription`, { personId, audience: "supplier" }), [supplierOrganisationId, personId]);
  const [note, setNote] = useState("");
  const noSub = sub.error?.code === "subscription.not_found";
  const nameOf = (code: string) => plans.data?.find((p) => p.code === code)?.name ?? code;
  const change = useAction((plan: PlanDto) => (sub.data
    ? api.patch<Subscription>(`/organisations/${supplierOrganisationId}/subscription`, { personId, planCode: plan.code, cancel: false })
    : api.post<Subscription>("/subscriptions", { personId, organisationId: supplierOrganisationId, planCode: plan.code })));
  const feature = (code: string) => { const key = `f_${code}` as PlanKey; return key in messages.en ? t(key) : code; };

  return (
    <Stack>
      {note && <Notice kind="success">{note}</Notice>}
      <Card title={t("subHeading")}>
        {sub.error && !noSub && <Notice kind="danger">{sub.error.message}</Notice>}
        {noSub && <p>{t("noSub")}</p>}
        {sub.loading && !sub.data && !sub.error && <p>{t("loading")}</p>}
        {sub.data && (
          <Stack>
            <p>{t("subLine", { plan: nameOf(sub.data.planCode), status: sub.data.status, from: formatDate(sub.data.periodStart), to: formatDate(sub.data.periodEnd) })}</p>
            {sub.data.pendingPlanCode && <Notice kind="info">{t("pending", { plan: nameOf(sub.data.pendingPlanCode) })}</Notice>}
            {sub.data.cancelAtPeriodEnd && <Notice kind="warning">{t("cancelling")}</Notice>}
          </Stack>
        )}
      </Card>
      <Card title={t("plansHeading")}>
        {plans.error && <Notice kind="danger">{plans.error.message}</Notice>}
        {plans.loading && !plans.data ? <p>{t("loading")}</p> : (
          <Table
            rows={plans.data ?? []}
            rowKey={(p) => p.id}
            empty={t("plansEmpty")}
            columns={[
              { header: t("colPlan"), cell: (p) => <>{p.name} {sub.data?.planCode === p.code && <Badge>{t("current")}</Badge>}</> },
              { header: t("colPrice"), cell: (p) => (p.priceCents === 0 ? t("free") : formatRand(p.priceCents)), align: "right" },
              { header: t("colIncludes"), cell: (p) => (p.features.length === 0 ? t("includesNothing") : p.features.map((f) => feature(f.feature)).join(", ")) },
              {
                header: "",
                cell: (p) => sub.data?.planCode === p.code ? "" : (
                  <Button variant="secondary" disabled={change.busy} aria-label={t("choose", { name: p.name })}
                    onClick={() => void change.run(p).then((r) => { if (r.ok) { setNote(t("subscribed", { plan: p.name })); sub.reload(); } })}>{t("chooseShort")}</Button>
                ),
              },
            ]}
          />
        )}
        {change.error && <Notice kind="danger">{change.error.message}</Notice>}
      </Card>
    </Stack>
  );
}
