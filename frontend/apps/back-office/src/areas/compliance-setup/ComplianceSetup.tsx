import { formatDate, saveFile, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { messages, type ComplianceSetupKey } from "./messages";
import { useTillCaller } from "../selling-common/TillCaller";
import { uploadWith, type DocumentUploader } from "./upload";

interface SetupStep { item: string; done: boolean; completedAt: string | null; note: string | null }
interface Checklist { steps: SetupStep[]; allDone: boolean; shopStatus: string }
export interface ComplianceDocument { id: string; typeCode: string; fileName: string; expiresOn: string | null; daysLeft: number | null; status: string }
interface Lookup { code: string; names: Record<string, string>; isActive: boolean }

const statusKeys: Record<string, ComplianceSetupKey> = { valid: "status_valid", expiring_soon: "status_expiring_soon", expired: "status_expired" };

type Saver = (file: { blob: Blob; fileName: string }) => void;

export function ComplianceSetup({ upload, save = saveFile }: { upload?: DocumentUploader; save?: Saver }) {
  const t = useMessages(messages);
  const api = useApi();
  const [tab, setTab] = useState<"setup" | "documents">("setup");
  return (
    <Stack>
      <Inline>
        <Button variant={tab === "setup" ? "primary" : "secondary"} aria-pressed={tab === "setup"} onClick={() => setTab("setup")}>{t("tabSetup")}</Button>
        <Button variant={tab === "documents" ? "primary" : "secondary"} aria-pressed={tab === "documents"} onClick={() => setTab("documents")}>{t("tabDocuments")}</Button>
      </Inline>
      {tab === "setup" ? <Setup /> : <Documents upload={upload ?? uploadWith(api)} save={save} />}
    </Stack>
  );
}

function Setup() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const list = useLoad(() => api.get<Checklist>(`/shops/${shopId}/setup`), [shopId]);
  const [message, setMessage] = useState("");
  const mark = useAction((item: string) => api.put<unknown>(`/shops/${shopId}/setup/${encodeURIComponent(item)}`, { agentPersonId: personId, note: null }));

  const label = (item: string) => {
    const key = `step_${item}` as ComplianceSetupKey;
    return key in stepLabels ? t(key) : item.replace(/_/g, " ");
  };

  async function done(item: string) {
    setMessage("");
    const r = await mark.run(item);
    if (r.ok) {
      setMessage(t("stepSaved", { step: label(item) }));
      list.reload();
    }
  }

  const data = list.data;
  return (
    <Card title={t("setupHeading")}>
      <Stack>
        <p>{t("setupHint")}</p>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !data ? <p>{t("loading")}</p> : data && (
          <>
            <Table
              rows={data.steps}
              rowKey={(s) => s.item}
              empty={t("setupEmpty")}
              columns={[
                { header: t("colStep"), cell: (s) => label(s.item) },
                { header: t("colDone"), cell: (s) => <Badge>{s.done ? t("done") : t("notDone")}</Badge> },
                { header: t("colNote"), cell: (s) => s.note ?? "" },
                {
                  header: t("colActions"),
                  cell: (s) => (s.done ? "" : <Button variant="secondary" disabled={mark.busy} aria-label={t("markDoneFor", { step: label(s.item) })} onClick={() => void done(s.item)}>{t("markDone")}</Button>),
                },
              ]}
            />
            {data.allDone && <Notice kind="success">{t("allDone")}</Notice>}
            <p>{t("shopStatus", { status: data.shopStatus })}</p>
          </>
        )}
        {mark.error && <Notice kind="danger">{mark.error.message}</Notice>}
        {message && <Notice kind="success">{message}</Notice>}
      </Stack>
    </Card>
  );
}

const stepLabels = {
  step_device_check: 1, step_shop_details: 1, step_document_photos: 1, step_catalogue_import: 1, step_staff_pins: 1, step_test_sale: 1, step_training_signoff: 1,
} as const;

function Documents({ upload, save }: { upload: DocumentUploader; save: Saver }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, language } = useSession();
  const caller = useTillCaller();
  const { deviceId, staffId: personId } = caller;
  const docs = useLoad(() => api.get<ComplianceDocument[]>(`/shops/${shopId}/compliance/documents`), [shopId]);
  const types = useLoad(() => api.get<Lookup[]>("/lookups/document_types"), []);
  const [typeCode, setTypeCode] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [expiresOn, setExpiresOn] = useState("");
  const [errors, setErrors] = useState<{ type?: string; file?: string }>({});
  const [message, setMessage] = useState("");
  const [fileKey, setFileKey] = useState(0);

  const send = useAction(async (f: File) => upload({ shopId, deviceId, staffId: personId, typeCode, expiresOn, file: f }));
  const remove = useAction((id: string) => api.delete<unknown>(`/shops/${shopId}/compliance/documents/${id}`, { deviceId, staffId: personId }));
  const fetchFile = useAction((id: string) => api.download(`/shops/${shopId}/compliance/documents/${id}/file`));

  useEffect(() => { setMessage(""); }, [shopId]);

  const typeName = (code: string) => {
    const l = (types.data ?? []).find((x) => x.code === code);
    return l ? l.names[language] ?? l.names.en ?? code : code;
  };

  async function submit() {
    setMessage("");
    const e: typeof errors = {};
    if (typeCode === "") e.type = t("docTypeRequired");
    if (!file) e.file = t("fileRequired");
    setErrors(e);
    if (e.type || e.file || !file) return;
    const r = await send.run(file);
    if (r.ok) {
      setMessage(t("uploaded", { file: file.name }));
      setFile(null);
      setFileKey((k) => k + 1);
      setTypeCode("");
      setExpiresOn("");
      docs.reload();
    }
  }

  async function del(d: ComplianceDocument) {
    setMessage("");
    const r = await remove.run(d.id);
    if (r.ok) {
      setMessage(t("removed", { file: d.fileName }));
      docs.reload();
    }
  }

  async function open(d: ComplianceDocument) {
    const r = await fetchFile.run(d.id);
    if (r.ok) save({ blob: r.value.blob, fileName: r.value.fileName === "download" ? d.fileName : r.value.fileName });
  }

  return (
    <Stack>
      {caller.card}
      <Card title={t("docsHeading")}>
        <Stack>
          {docs.error && <Notice kind="danger">{docs.error.message}</Notice>}
          {docs.loading && !docs.data ? <p>{t("loading")}</p> : (
            <Table
              rows={docs.data ?? []}
              rowKey={(d) => d.id}
              empty={t("docsEmpty")}
              columns={[
                { header: t("colType"), cell: (d) => typeName(d.typeCode) },
                { header: t("colFile"), cell: (d) => d.fileName },
                { header: t("colExpires"), cell: (d) => (d.expiresOn ? `${formatDate(d.expiresOn)}${d.daysLeft !== null ? ` (${t("daysLeft", { n: d.daysLeft })})` : ""}` : t("noExpiry")) },
                { header: t("colStatus"), cell: (d) => <Badge>{statusKeys[d.status] ? t(statusKeys[d.status]!) : d.status}</Badge> },
                {
                  header: t("colActions"),
                  cell: (d) => (
                    <Inline>
                      <Button variant="secondary" disabled={fetchFile.busy} aria-label={t("openFileFor", { file: d.fileName })} onClick={() => void open(d)}>{t("openFile")}</Button>
                      <Button variant="quiet" disabled={remove.busy} aria-label={t("removeFor", { file: d.fileName })} onClick={() => void del(d)}>{t("remove")}</Button>
                    </Inline>
                  ),
                },
              ]}
            />
          )}
          {remove.error && <Notice kind="danger">{remove.error.message}</Notice>}
          {fetchFile.error && <Notice kind="danger">{fetchFile.error.message}</Notice>}
        </Stack>
      </Card>
      <Card title={t("uploadHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }} noValidate>
          <Stack>
            <p>{t("uploadHint")}</p>
            <Inline>
              <SelectField label={t("docType")} value={typeCode} error={errors.type} onChange={(e) => setTypeCode(e.target.value)}>
                <option value="">{t("docTypeChoose")}</option>
                {(types.data ?? []).filter((l) => l.isActive).map((l) => <option key={l.code} value={l.code}>{l.names[language] ?? l.names.en ?? l.code}</option>)}
              </SelectField>
              <TextField label={t("docFile")} key={fileKey} type="file" accept="image/jpeg,image/png,application/pdf" error={errors.file} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
              <TextField label={t("expiresOn")} type="date" value={expiresOn} onChange={(e) => setExpiresOn(e.target.value)} />
            </Inline>
            {send.error && <Notice kind="danger">{send.error.message}</Notice>}
            {message && <Notice kind="success">{message}</Notice>}
            <Button type="submit" disabled={send.busy || !caller.ready}>{t("upload")}</Button>
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}
