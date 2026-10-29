import { formatDate, formatRand, saveFile, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type ReportsKey } from "./messages";

interface Period {
  days: number;
  salesCents: number;
  profitCents: number;
  customers: number;
  averageSaleCents: number;
}
interface BestSeller { productId: string; name: string; units: number; revenueCents: number; profitCents: number }
interface StockRow { productId: string; name: string; onHand: number; daysLeft: number | null; expiryDate: string | null }
export interface Insights {
  today: Period;
  week: Period;
  month: Period;
  bestSellers: BestSeller[];
  runningLow: StockRow[];
  notSelling: StockRow[];
  expiring: StockRow[];
}
interface Branch { shopId: string; name: string; isHead: boolean }

export const REPORTS = [
  ["sales-by-day", "report_sales_by_day"],
  ["sales-by-product", "report_sales_by_product"],
  ["stock-on-hand", "report_stock_on_hand"],
  ["cash-ups", "report_cash_ups"],
  ["credit-book", "report_credit_book"],
  ["tips-by-staff", "report_tips_by_staff"],
] as const satisfies ReadonlyArray<readonly [string, ReportsKey]>;

type Saver = (file: { blob: Blob; fileName: string }) => void;

export function Reports({ save = saveFile }: { save?: Saver }) {
  return (
    <Stack>
      <InsightsCard />
      <DownloadCard save={save} />
    </Stack>
  );
}

function InsightsCard() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const insights = useLoad(() => api.get<Insights>(`/shops/${shopId}/insights`), [shopId]);
  const d = insights.data;
  const periods: [ReportsKey, Period][] = d ? [["periodToday", d.today], ["periodWeek", d.week], ["periodMonth", d.month]] : [];

  const stockTable = (title: ReportsKey, rows: StockRow[]) => (
    <Stack>
      <h3>{t(title)}</h3>
      <Table
        rows={rows}
        rowKey={(r) => r.productId}
        empty={t("listEmpty")}
        columns={[
          { header: t("colProduct"), cell: (r) => r.name },
          { header: t("colOnHand"), align: "right", cell: (r) => r.onHand },
          { header: t("colDaysLeft"), align: "right", cell: (r) => r.daysLeft ?? "" },
          { header: t("colExpiry"), cell: (r) => formatDate(r.expiryDate) },
        ]}
      />
    </Stack>
  );

  return (
    <Card title={t("insightsHeading")}>
      <Stack>
        {insights.error && <Notice kind="danger">{insights.error.message}</Notice>}
        {insights.loading && !d ? <p>{t("loading")}</p> : d && (
          <>
            <Table
              rows={periods}
              rowKey={([k]) => k}
              columns={[
                { header: t("colPeriod"), cell: ([k]) => t(k) },
                { header: t("colSales"), align: "right", cell: ([, p]) => formatRand(p.salesCents) },
                { header: t("colProfit"), align: "right", cell: ([, p]) => formatRand(p.profitCents) },
                { header: t("colCustomers"), align: "right", cell: ([, p]) => p.customers },
                { header: t("colAverage"), align: "right", cell: ([, p]) => formatRand(p.averageSaleCents) },
              ]}
            />
            <h3>{t("bestSellers")}</h3>
            <Table
              rows={d.bestSellers}
              rowKey={(b) => b.productId}
              empty={t("listEmpty")}
              columns={[
                { header: t("colProduct"), cell: (b) => b.name },
                { header: t("colUnits"), align: "right", cell: (b) => b.units },
                { header: t("colRevenue"), align: "right", cell: (b) => formatRand(b.revenueCents) },
                { header: t("colProfit"), align: "right", cell: (b) => formatRand(b.profitCents) },
              ]}
            />
            {stockTable("runningLow", d.runningLow)}
            {stockTable("notSelling", d.notSelling)}
            {stockTable("expiring", d.expiring)}
          </>
        )}
      </Stack>
    </Card>
  );
}

function DownloadCard({ save }: { save: Saver }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const branches = useLoad(() => api.get<Branch[]>(`/organisations/${shopId}/branches`, { callerPersonId: personId }), [shopId, personId]);
  const [code, setCode] = useState<string>("sales-by-day");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [format, setFormat] = useState("csv");
  const [branch, setBranch] = useState("");
  const [rangeError, setRangeError] = useState("");
  const [done, setDone] = useState("");

  const run = useAction(() => api.download(`/shops/${shopId}/reports/${code}`, { from, to, format, branch: branch === "" ? undefined : branch, callerPersonId: personId }));

  async function submit() {
    setDone("");
    if (from !== "" && to !== "" && from > to) { setRangeError(t("rangeInvalid")); return; }
    setRangeError("");
    const r = await run.run();
    if (r.ok) {
      save(r.value);
      setDone(t("downloaded", { name: r.value.fileName }));
    }
  }

  // The branch list needs the Pro plan. Without it the person can still download this shop's own report.
  const others = (branches.data ?? []).filter((b) => b.shopId !== shopId);

  return (
    <Card title={t("downloadHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <SelectField label={t("report")} value={code} onChange={(e) => setCode(e.target.value)}>
              {REPORTS.map(([c, key]) => <option key={c} value={c}>{t(key)}</option>)}
            </SelectField>
            <TextField label={t("from")} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <TextField label={t("to")} type="date" value={to} error={rangeError} onChange={(e) => setTo(e.target.value)} />
          </Inline>
          <Inline>
            <SelectField label={t("format")} value={format} onChange={(e) => setFormat(e.target.value)}>
              <option value="csv">{t("formatCsv")}</option>
              <option value="xlsx">{t("formatXlsx")}</option>
              <option value="pdf">{t("formatPdf")}</option>
            </SelectField>
            <SelectField label={t("branch")} value={branch} onChange={(e) => setBranch(e.target.value)}>
              <option value="">{t("branchThis")}</option>
              {others.length > 0 && <option value="all">{t("branchAll")}</option>}
              {others.map((b) => <option key={b.shopId} value={b.shopId}>{b.name}</option>)}
            </SelectField>
            <Button type="submit" disabled={run.busy}>{run.busy ? t("downloading") : t("download")}</Button>
          </Inline>
          {run.error && <Notice kind="danger">{run.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}
