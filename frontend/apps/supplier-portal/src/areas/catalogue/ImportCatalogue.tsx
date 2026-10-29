import { useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table } from "@tillknowit/design-system";
import { useRef, useState } from "react";
import { postForm, type PostForm } from "../../shared/postForm";
import { messages } from "./messages";

export interface CatalogueLine { row: number; barcode: string | null; name: string; unitsPerCase: number; casePriceCents: number; action: string; issues: string[] }
export interface CatalogueUpload {
  applied: boolean; created: number; updated: number; unchanged: number; errors: number; lines: CatalogueLine[]; headers: string[]; mapping: Record<string, string>;
}

/** Preview first (apply false), then the same file again with apply true: the server's own two-step import. */
export function ImportCatalogue({ supplierId, upload = postForm }: { supplierId: string; upload?: PostForm }) {
  const t = useMessages(messages);
  const { personId } = useSession();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<CatalogueUpload | null>(null);

  async function send(apply: boolean) {
    const file = input.current?.files?.[0];
    if (!file) { setError(t("chooseFile")); return; }
    setError("");
    setBusy(true);
    const form = new FormData();
    form.append("callerPersonId", personId);
    form.append("apply", apply ? "true" : "false");
    form.append("file", file);
    const r = await upload<CatalogueUpload>(`/suppliers/${supplierId}/products/import`, form);
    setBusy(false);
    if (r.ok) setResult(r.value); else setError(r.message);
  }

  return (
    <Card title={t("importHeading")}>
      <Stack>
        <p>{t("importIntro")}</p>
        <label>
          {t("file")}
          <input ref={input} type="file" accept=".csv,.xlsx,.xls" onChange={() => { setResult(null); setError(""); }} />
        </label>
        <Inline>
          <Button variant="secondary" disabled={busy} onClick={() => void send(false)}>{t("preview")}</Button>
          <Button disabled={busy || !result || result.applied} onClick={() => void send(true)}>{t("apply")}</Button>
        </Inline>
        {error && <Notice kind="danger">{error}</Notice>}
        {result && (
          <>
            <Notice kind="info">{t(result.applied ? "appliedOf" : "previewOf", { created: result.created, updated: result.updated, unchanged: result.unchanged, errors: result.errors })}</Notice>
            <Table
              rows={result.lines}
              rowKey={(l) => String(l.row)}
              empty={t("noLines")}
              columns={[
                { header: t("colRow"), cell: (l) => l.row, align: "right" },
                { header: t("colName"), cell: (l) => l.name },
                { header: t("colAction"), cell: (l) => <Badge>{l.action}</Badge> },
                { header: t("colIssues"), cell: (l) => l.issues.join(", ") },
              ]}
            />
          </>
        )}
      </Stack>
    </Card>
  );
}
