import { formatDate, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface TrendRow { weekStart: string; category: string; units: number; revenueBand: string; shops: number }
export interface InsightReport { area: string; rows: TrendRow[]; note: string }

const day = (d: Date) => d.toISOString().slice(0, 10);

export function Trends() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, supplierOrganisationId } = useSession();
  const [area, setArea] = useState("");
  const [category, setCategory] = useState("");
  const [from, setFrom] = useState(() => day(new Date(Date.now() - 56 * 86_400_000)));
  const [to, setTo] = useState(() => day(new Date()));
  const [problem, setProblem] = useState("");
  const [report, setReport] = useState<InsightReport | null>(null);
  const ask = useAction(() => api.get<InsightReport>("/insights/reports", {
    personId, organisationId: supplierOrganisationId, audience: "supplier", area: area.trim(), category: category.trim(), from, to,
  }));

  async function submit() {
    if (area.trim() === "") { setProblem(t("needArea")); return; }
    if (from === "" || to === "") { setProblem(t("needDates")); return; }
    setProblem("");
    setReport(null);
    const r = await ask.run();
    if (r.ok) setReport(r.value);
  }

  return (
    <Stack>
      <Card title={t("heading")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <p>{t("intro")}</p>
            <Inline>
              <TextField label={t("area")} hint={t("areaHint")} value={area} onChange={(e) => setArea(e.target.value)} required />
              <TextField label={t("category")} value={category} onChange={(e) => setCategory(e.target.value)} />
              <TextField label={t("from")} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
              <TextField label={t("to")} type="date" value={to} onChange={(e) => setTo(e.target.value)} />
              <Button type="submit" disabled={ask.busy}>{ask.busy ? t("busy") : t("request")}</Button>
            </Inline>
            {problem && <Notice kind="danger">{problem}</Notice>}
            {ask.error?.code === "plan.feature_not_included" && <Notice kind="warning"><strong>{t("planTitle")}</strong> {t("planBody")}</Notice>}
            {ask.error && ask.error.code !== "plan.feature_not_included" && <Notice kind="danger">{ask.error.message}</Notice>}
          </Stack>
        </form>
      </Card>
      {report && (
        <Card title={t("results", { area: report.area })}>
          <Table
            rows={report.rows}
            rowKey={(r) => `${r.weekStart}|${r.category}`}
            empty={t("empty")}
            columns={[
              { header: t("colWeek"), cell: (r) => formatDate(r.weekStart) },
              { header: t("colCategory"), cell: (r) => r.category },
              { header: t("colUnits"), cell: (r) => r.units, align: "right" },
              { header: t("colRevenue"), cell: (r) => r.revenueBand },
              { header: t("colShops"), cell: (r) => r.shops, align: "right" },
            ]}
          />
          <Notice kind="info">{report.note}</Notice>
          {report.rows.length > 0 && <p>{t("shopsNote")}</p>}
        </Card>
      )}
      <Card title={t("minHeading")}>
        <p>{t("minRule")}</p>
      </Card>
    </Stack>
  );
}
