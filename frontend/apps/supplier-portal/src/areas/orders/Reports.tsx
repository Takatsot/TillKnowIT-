import { saveFile, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

type Save = (file: { blob: Blob; fileName: string }) => void;

export function Statement({ supplierId, save = saveFile }: { supplierId: string; save?: Save }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [format, setFormat] = useState("csv");
  const [done, setDone] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function download() {
    setBusy(true);
    setDone("");
    setError("");
    const r = await api.download(`/suppliers/${supplierId}/statement`, { callerPersonId: personId, from, to, format });
    setBusy(false);
    if (!r.ok) { setError(r.message); return; }
    save(r.value);
    setDone(t("downloaded", { name: r.value.fileName }));
  }

  return (
    <Card title={t("statementHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void download(); }}>
        <Stack>
          <p>{t("statementIntro")}</p>
          <Inline>
            <TextField label={t("from")} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <TextField label={t("to")} type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            <SelectField label={t("format")} value={format} onChange={(e) => setFormat(e.target.value)}>
              <option value="csv">CSV</option>
              <option value="xlsx">Excel (xlsx)</option>
              <option value="pdf">PDF</option>
            </SelectField>
            <Button type="submit" disabled={busy}>{t("download")}</Button>
          </Inline>
          {error && <Notice kind="danger">{error}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

interface RatingSummary { count: number; averageScore: number | null; onTimePercent: number | null; completePercent: number | null; recentComments: string[] }

export function Ratings({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const r = useLoad(() => api.get<RatingSummary>(`/suppliers/${supplierId}/ratings`, { callerPersonId: personId }), [supplierId, personId]);
  const d = r.data;
  return (
    <Card title={t("ratingsHeading")}>
      {r.error && <Notice kind="danger">{r.error.message}</Notice>}
      {r.loading && !d ? <p>{t("loading")}</p> : d && (d.count === 0 ? <p>{t("ratingsNone")}</p> : (
        <Stack>
          <p>{t("ratingsCount", { count: d.count })}</p>
          {d.averageScore === null ? <p>{t("ratingsNotEnough")}</p> : (
            <ul>
              <li>{t("average", { score: d.averageScore.toFixed(1) })}</li>
              {d.onTimePercent !== null && <li>{t("onTime", { n: d.onTimePercent })}</li>}
              {d.completePercent !== null && <li>{t("complete", { n: d.completePercent })}</li>}
            </ul>
          )}
          {d.recentComments.length > 0 && (<><h3>{t("comments")}</h3><ul>{d.recentComments.map((c, i) => <li key={i}>{c}</li>)}</ul></>)}
        </Stack>
      ))}
    </Card>
  );
}

interface Demand { minShops: number; areas: { areaCode: string; products: { supplierProductId: string; name: string; shopsLow: number }[] }[] }

export function DemandView({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const r = useLoad(() => api.get<Demand>(`/suppliers/${supplierId}/demand`, { callerPersonId: personId }), [supplierId, personId]);
  const d = r.data;
  return (
    <Card title={t("demandHeading")}>
      {r.error && <Notice kind="danger">{r.error.message}</Notice>}
      {r.loading && !d ? <p>{t("loading")}</p> : d && (
        <Stack>
          <p>{t("demandIntro", { min: d.minShops })}</p>
          {d.areas.length === 0 && <p>{t("demandEmpty", { min: d.minShops })}</p>}
          {d.areas.map((a) => (
            <section key={a.areaCode} aria-label={t("area", { code: a.areaCode })}>
              <h3>{t("area", { code: a.areaCode })}</h3>
              <Table rows={a.products} rowKey={(p) => p.supplierProductId} columns={[
                { header: t("colProduct"), cell: (p) => p.name },
                { header: t("colShops"), cell: (p) => p.shopsLow, align: "right" },
              ]} />
            </section>
          ))}
        </Stack>
      )}
    </Card>
  );
}
