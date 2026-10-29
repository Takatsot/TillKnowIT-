import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface ApiClient {
  id: string;
  name: string;
  keyPrefix: string;
  scopes: string[];
  rateLimitPerMinute: number;
  revoked: boolean;
}

export interface ClientKey {
  clientId: string;
  name: string;
  keyPrefix: string;
  key: string;
  scopes: string[];
  rateLimitPerMinute: number;
}

/** The scopes the server knows (Platform.Domain ApiScopes). */
export const scopes = ["sales.daily", "sales.monthly"] as const;

export function PartnerApi() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<ApiClient[]>("/admin/api-clients", { callerPersonId: personId }), [personId]);
  const [shown, setShown] = useState<{ key: ClientKey; rotated: boolean } | null>(null);
  const [note, setNote] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);

  const rotate = useAction((c: ApiClient) => api.post<ClientKey>(`/admin/api-clients/${c.id}/rotate-key`, undefined, { callerPersonId: personId }));
  const revoke = useAction((c: ApiClient) => api.post<ApiClient>(`/admin/api-clients/${c.id}/revoke`, undefined, { callerPersonId: personId }));

  async function doRotate(c: ApiClient) {
    setNote("");
    const r = await rotate.run(c);
    if (r.ok) {
      setShown({ key: r.value, rotated: true });
      list.reload();
    }
  }

  async function doRevoke(c: ApiClient) {
    setNote("");
    const r = await revoke.run(c);
    setConfirming(null);
    if (r.ok) {
      setNote(t("revokedDone", { name: c.name }));
      if (shown?.key.clientId === c.id) setShown(null);
      list.reload();
    }
  }

  return (
    <Stack>
      {shown && <KeyOnce shown={shown} onDone={() => setShown(null)} />}
      <Card title={t("heading")}>
        <p>{t("intro")}</p>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {rotate.error && <Notice kind="danger">{rotate.error.message}</Notice>}
        {revoke.error && <Notice kind="danger">{revoke.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(c) => c.id}
            empty={t("empty")}
            columns={[
              { header: t("colName"), cell: (c) => c.name },
              { header: t("colPrefix"), cell: (c) => <code>{c.keyPrefix}</code> },
              { header: t("colScopes"), cell: (c) => c.scopes.join(", ") },
              { header: t("colRate"), cell: (c) => c.rateLimitPerMinute, align: "right" },
              { header: t("colStatus"), cell: (c) => <Badge>{c.revoked ? t("revoked") : t("active")}</Badge> },
              {
                header: t("colActions"),
                cell: (c) => c.revoked ? null : confirming === c.id ? (
                  <Inline>
                    <Button variant="secondary" disabled={revoke.busy} onClick={() => void doRevoke(c)}>{t("confirmRevoke", { name: c.name })}</Button>
                    <Button variant="quiet" onClick={() => setConfirming(null)}>{t("keep")}</Button>
                  </Inline>
                ) : (
                  <Inline>
                    <Button variant="secondary" aria-label={`${t("rotate")} ${c.name}`} disabled={rotate.busy} onClick={() => void doRotate(c)}>{t("rotate")}</Button>
                    <Button variant="quiet" aria-label={`${t("revoke")} ${c.name}`} onClick={() => setConfirming(c.id)}>{t("revoke")}</Button>
                  </Inline>
                ),
              },
            ]}
          />
        )}
      </Card>
      <Register onKey={(key) => { setNote(""); setShown({ key, rotated: false }); list.reload(); }} />
    </Stack>
  );
}

function KeyOnce({ shown, onDone }: { shown: { key: ClientKey; rotated: boolean }; onDone: () => void }) {
  const t = useMessages(messages);
  const [copy, setCopy] = useState<"idle" | "ok" | "failed">("idle");

  async function copyKey() {
    try {
      await navigator.clipboard.writeText(shown.key.key);
      setCopy("ok");
    } catch {
      setCopy("failed");
    }
  }

  return (
    <Card title={t("keyHeading", { name: shown.key.name })}>
      <Stack>
        <Notice kind="warning">{t("keyWarning")}</Notice>
        {shown.rotated && <p>{t("rotateNote")}</p>}
        <TextField label={t("keyLabel")} value={shown.key.key} readOnly onFocus={(e) => e.currentTarget.select()} />
        <Inline>
          <Button onClick={() => void copyKey()}>{t("copy")}</Button>
          <Button variant="secondary" onClick={onDone}>{t("keySaved")}</Button>
        </Inline>
        {copy === "ok" && <Notice kind="success">{t("copied")}</Notice>}
        {copy === "failed" && <Notice kind="warning">{t("copyFailed")}</Notice>}
      </Stack>
    </Card>
  );
}

function Register({ onKey }: { onKey: (key: ClientKey) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [ticked, setTicked] = useState<Record<string, boolean>>({});
  const [rate, setRate] = useState("60");
  const [local, setLocal] = useState<{ rate?: string; scopes?: string }>({});
  const [note, setNote] = useState("");
  const create = useAction((body: unknown) => api.post<ClientKey>("/admin/api-clients", body));

  async function submit() {
    const chosen = scopes.filter((s) => ticked[s]);
    const n = Number(rate);
    const bad: typeof local = {};
    if (!/^\d+$/.test(rate.trim()) || n < 1 || n > 6000) bad.rate = t("rateInvalid");
    if (chosen.length === 0) bad.scopes = t("scopeRequired");
    setLocal(bad);
    if (bad.rate || bad.scopes) return;
    const r = await create.run({ callerPersonId: personId, name: name.trim(), contactEmail: email.trim() === "" ? null : email.trim(), scopes: chosen, rateLimitPerMinute: n });
    if (r.ok) {
      setNote(t("registered", { name: r.value.name }));
      setName("");
      setEmail("");
      setTicked({});
      onKey(r.value);
    }
  }

  return (
    <Card title={t("newClient")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("name")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("email")} type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </Inline>
          <fieldset>
            <legend>{t("scopesLegend")}</legend>
            <Stack>
              {scopes.map((s) => <CheckField key={s} label={t(s)} checked={Boolean(ticked[s])} onChange={(e) => setTicked({ ...ticked, [s]: e.target.checked })} />)}
            </Stack>
            {local.scopes ? <Notice kind="danger">{local.scopes}</Notice> : <small>{t("scopesHint")}</small>}
          </fieldset>
          <TextField label={t("rate")} hint={t("rateHint")} inputMode="numeric" value={rate} error={local.rate ?? null} onChange={(e) => setRate(e.target.value)} required />
          {create.error && <Notice kind="danger">{create.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          <Button type="submit" disabled={create.busy || name.trim() === ""}>{t("register")}</Button>
        </Stack>
      </form>
    </Card>
  );
}
