import { formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Notice, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { bigStyle, itemStyle, listStyle, mutedStyle } from "../../shared/ChildScope";
import type { Child } from "../../shared/types";
import { uploadPhoto, type PhotoUploader } from "../../shared/upload";
import { CONSENT_VERSION, messages } from "./messages";

export function Children({ upload = uploadPhoto }: { upload?: PhotoUploader }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Child[]>("/me/children", { callerPersonId: personId }), [personId]);
  const [code, setCode] = useState("");
  const [agreed, setAgreed] = useState(false);
  const [added, setAdded] = useState("");
  const [editing, setEditing] = useState("");

  const link = useAction(() => api.post<Child>("/guardianships", { parentPersonId: personId, linkCode: code.trim(), consent: agreed, consentVersion: CONSENT_VERSION }));

  async function submit() {
    setAdded("");
    const r = await link.run();
    if (r.ok) {
      setAdded(t("linked", { name: r.value.name }));
      setCode("");
      setAgreed(false);
      list.reload();
    }
  }

  return (
    <Stack>
      <Card title={t("heading")}>
        {list.error && (
          <>
            <Notice kind="danger">{list.error.message}</Notice>
            <Button variant="secondary" onClick={list.reload}>{t("reload")}</Button>
          </>
        )}
        {!list.data && !list.error && <p>{t("loading")}</p>}
        {list.data && list.data.length === 0 && <p>{t("empty")}</p>}
        {list.data && list.data.length > 0 && (
          <ul style={listStyle}>
            {list.data.map((c) => (
              <li key={c.holderId} style={itemStyle}>
                <strong>{c.name}</strong>
                <span style={mutedStyle}>{c.school}{c.grade ? ` · ${t("classLine", { grade: c.grade, className: c.className ?? "" })}` : ""}</span>
                <span style={mutedStyle}>{t("balance")}</span>
                <p style={bigStyle}>{formatRand(c.balanceCents)}</p>
                <span>{c.dailyLimitCents === null ? t("noLimit") : `${t("dailyLimit")}: ${formatRand(c.dailyLimitCents)}`}</span>
                {c.walletStatus !== "active" && <Badge>{t("frozen")}</Badge>}
                <Button variant="secondary" onClick={() => setEditing(editing === c.holderId ? "" : c.holderId)} aria-expanded={editing === c.holderId}>
                  {editing === c.holderId ? t("close") : t("edit")}
                </Button>
                {editing === c.holderId && <Editor child={c} upload={upload} onDone={list.reload} />}
              </li>
            ))}
          </ul>
        )}
      </Card>
      <Card title={t("add")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <p style={mutedStyle}>{t("addHelp")}</p>
            <TextField label={t("code")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" autoCapitalize="characters" required />
            <CheckField label={t("consent")} checked={agreed} onChange={(e) => setAgreed(e.target.checked)} />
            {link.error && <Notice kind="danger">{link.error.message}</Notice>}
            {added && <Notice kind="success">{added}</Notice>}
            <Button type="submit" disabled={link.busy || code.trim() === "" || !agreed}>{t("link")}</Button>
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}

function Editor({ child, upload, onDone }: { child: Child; upload: PhotoUploader; onDone: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [form, setForm] = useState({ firstName: "", lastName: "", grade: "", className: "" });
  const [file, setFile] = useState<File | null>(null);
  const [note, setNote] = useState("");

  const blank = (v: string) => (v.trim() === "" ? null : v.trim());
  const save = useAction(() => api.patch(`/holders/${child.holderId}`, {
    callerPersonId: personId, firstName: blank(form.firstName), lastName: blank(form.lastName), grade: blank(form.grade), className: blank(form.className),
  }));
  const send = useAction(async () => (file ? upload(child.holderId, personId, file) : { ok: false as const, code: "photo.none", message: t("photoNone") }));

  return (
    <Stack>
      <form onSubmit={(e) => { e.preventDefault(); setNote(""); void save.run().then((r) => { if (r.ok) { setNote(t("saved")); onDone(); } }); }}>
        <Stack>
          <strong>{t("details")}</strong>
          <p style={mutedStyle}>{t("detailsHelp")}</p>
          <TextField label={t("firstName")} value={form.firstName} onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
          <TextField label={t("lastName")} value={form.lastName} onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
          <TextField label={t("grade")} value={form.grade} onChange={(e) => setForm({ ...form, grade: e.target.value })} />
          <TextField label={t("className")} value={form.className} onChange={(e) => setForm({ ...form, className: e.target.value })} />
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          <Button type="submit" disabled={save.busy}>{t("save")}</Button>
        </Stack>
      </form>
      <form onSubmit={(e) => { e.preventDefault(); setNote(""); void send.run().then((r) => { if (r.ok) { setNote(t("photoSaved")); onDone(); } }); }}>
        <Stack>
          <TextField label={t("photo")} hint={t("photoHelp")} type="file" accept="image/jpeg,image/png" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          {send.error && <Notice kind="danger">{send.error.message}</Notice>}
          <Button type="submit" disabled={send.busy}>{t("photoSave")}</Button>
        </Stack>
      </form>
      {note && <Notice kind="success">{note}</Notice>}
    </Stack>
  );
}
