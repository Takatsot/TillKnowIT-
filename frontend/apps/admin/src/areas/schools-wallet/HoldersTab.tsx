import { saveFile, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, Stack, Table } from "@tillknowit/design-system";
import { useState } from "react";
import { AddHolderCard, EditHolderCard } from "./HolderForms";
import { messages } from "./messages";
import type { Holder, HolderUploader, ImportResult, School } from "./types";

const csvCell = (v: string | null) => `"${(v ?? "").replace(/"/g, "\"\"")}"`;

export function HoldersTab({ school, upload }: { school: School; upload: HolderUploader }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Holder[]>(`/schools/${school.id}/holders`, { callerPersonId: personId }), [school.id, personId]);
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Holder | null>(null);

  async function send() {
    if (!file) return;
    setBusy(true);
    setError(null);
    setResult(null);
    const r = await upload(school.id, personId, file);
    setBusy(false);
    if (r.ok) {
      setResult(r.value);
      list.reload();
    } else {
      setError(r.message);
    }
  }

  function saveCodes() {
    const lines = ["Name,Grade,Class,Link code", ...result!.linkCodes.map((c) => [c.name, c.grade, c.className, c.linkCode].map(csvCell).join(","))];
    saveFile({ blob: new Blob([lines.join("\r\n")], { type: "text/csv" }), fileName: `link-codes-${school.name}.csv` });
  }

  const problems = result?.rows.filter((r) => r.outcome !== "created") ?? [];

  return (
    <Stack>
      <AddHolderCard school={school} onAdded={list.reload} />
      {editing && <EditHolderCard key={editing.id} holder={editing} onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); list.reload(); }} />}
      <Card title={t("importHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); void send(); }}>
          <Stack>
            <p>{t("importHint")}</p>
            <label>
              {t("file")}
              <input type="file" accept=".csv,.xlsx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
            </label>
            {error && <Notice kind="danger">{error}</Notice>}
            <Button type="submit" disabled={busy || !file}>{busy ? t("importing") : t("importRun")}</Button>
            {result && <Notice kind={result.invalid > 0 ? "warning" : "success"}>{t("importSummary", { created: result.created, duplicates: result.duplicates, invalid: result.invalid })}</Notice>}
            {problems.length > 0 && (
              <>
                <h3>{t("importProblems")}</h3>
                <Table
                  rows={problems}
                  rowKey={(r) => String(r.row)}
                  columns={[
                    { header: t("colRow"), cell: (r) => r.row, align: "right" },
                    { header: t("colName"), cell: (r) => r.name },
                    { header: t("colOutcome"), cell: (r) => r.outcome },
                    { header: t("colDetail"), cell: (r) => r.detail ?? "" },
                  ]}
                />
              </>
            )}
            {result && result.linkCodes.length > 0 && (
              <>
                <h3>{t("codesHeading")}</h3>
                <Notice kind="warning">{t("codesWarning")}</Notice>
                <Table
                  rows={result.linkCodes}
                  rowKey={(c) => c.holderId}
                  columns={[
                    { header: t("colName"), cell: (c) => c.name },
                    { header: t("colGrade"), cell: (c) => c.grade ?? "" },
                    { header: t("colClass"), cell: (c) => c.className ?? "" },
                    { header: t("colCode"), cell: (c) => <code>{c.linkCode}</code> },
                  ]}
                />
                <Button variant="secondary" onClick={saveCodes}>{t("saveCodes")}</Button>
              </>
            )}
          </Stack>
        </form>
      </Card>
      <Card title={t("holdersHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(h) => h.id}
            empty={t("holdersEmpty")}
            columns={[
              { header: t("colName"), cell: (h) => `${h.firstName} ${h.lastName}` },
              { header: t("colType"), cell: (h) => h.type },
              { header: t("colGrade"), cell: (h) => h.grade ?? "" },
              { header: t("colClass"), cell: (h) => h.className ?? "" },
              { header: t("colStatus"), cell: (h) => <Badge>{h.status}</Badge> },
              { header: t("colActions"), cell: (h) => <Button variant="quiet" aria-label={t("editHolderFor", { name: `${h.firstName} ${h.lastName}` })} onClick={() => setEditing(h)}>{t("editHolder")}</Button> },
            ]}
          />
        )}
      </Card>
    </Stack>
  );
}
