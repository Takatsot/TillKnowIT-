import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, Table, TextAreaField, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface PackFeature {
  flag: string;
  settingsJson: string | null;
}

export interface Pack {
  id: string;
  code: string;
  name: string;
  description: string | null;
  slipTemplateCode: string;
  version: number;
  isActive: boolean;
  features: PackFeature[];
}

/** The server's FeatureFlags (Platform.Domain/BusinessPacks.cs). The server refuses a flag it does not know. */
export const featureFlags = [
  "split_payments", "refunds", "open_tickets", "tables", "kitchen", "tips", "variants", "modifiers", "promotions", "weight", "price_embedded_barcodes",
  "appointments", "commission", "quotes", "layby", "serial_numbers", "customers", "loyalty", "vouchers", "printer", "cash_drawer", "scale", "message_bundles",
] as const;

interface Draft {
  code: string;
  isNew: boolean;
  name: string;
  description: string;
  slip: string;
  on: Record<string, boolean>;
  settings: Record<string, string>;
}

const blank: Draft = { code: "", isNew: true, name: "", description: "", slip: "slip.sale", on: {}, settings: {} };

function toDraft(p: Pack): Draft {
  return {
    code: p.code, isNew: false, name: p.name, description: p.description ?? "", slip: p.slipTemplateCode,
    on: Object.fromEntries(p.features.map((f) => [f.flag, true])),
    settings: Object.fromEntries(p.features.map((f) => [f.flag, f.settingsJson ?? ""])),
  };
}

export function Packs() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Pack[]>("/admin/business-packs", { callerPersonId: personId }), [personId]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [note, setNote] = useState("");

  const save = useAction(() => {
    const d = draft!;
    const body = {
      callerPersonId: personId, code: d.code.trim(), name: d.name, description: d.description.trim() === "" ? null : d.description.trim(), slipTemplateCode: d.slip,
      features: featureFlags.filter((f) => d.on[f]).map((f) => ({ flag: f, settingsJson: (d.settings[f] ?? "").trim() === "" ? null : d.settings[f]!.trim() })),
    };
    return d.isNew ? api.post<Pack>("/admin/business-packs", body) : api.put<Pack>(`/admin/business-packs/${d.code}`, body);
  });
  const toggle = useAction((p: Pack) => api.post<Pack>(`/admin/business-packs/${p.code}/active`, undefined, { callerPersonId: personId, active: !p.isActive }));

  async function submit() {
    const isNew = draft!.isNew;
    const r = await save.run();
    if (r.ok) {
      setNote(isNew ? t("created", { name: r.value.name }) : t("saved", { name: r.value.name, version: r.value.version }));
      setDraft(null);
      list.reload();
    }
  }

  async function flip(p: Pack) {
    const r = await toggle.run(p);
    if (r.ok) {
      setNote(t(r.value.isActive ? "switchedOn" : "switchedOff", { name: r.value.name }));
      list.reload();
    }
  }

  const change = (patch: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...patch } : d));

  return (
    <Stack>
      <Card title={t("heading")}>
        <p>{t("intro")}</p>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {toggle.error && <Notice kind="danger">{toggle.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(p) => p.code}
            empty={t("empty")}
            columns={[
              { header: t("colName"), cell: (p) => p.name },
              { header: t("colCode"), cell: (p) => p.code },
              { header: t("colVersion"), cell: (p) => p.version },
              { header: t("colFeatures"), cell: (p) => p.features.length },
              { header: t("colSlip"), cell: (p) => p.slipTemplateCode },
              { header: t("colStatus"), cell: (p) => <Badge>{p.isActive ? t("active") : t("inactive")}</Badge> },
              {
                header: t("colActions"),
                cell: (p) => (
                  <Inline>
                    <Button variant="secondary" aria-label={`${t("edit")} ${p.name}`} onClick={() => { setNote(""); setDraft(toDraft(p)); }}>{t("edit")}</Button>
                    <Button variant="quiet" aria-label={`${p.isActive ? t("switchOff") : t("switchOn")} ${p.name}`} disabled={toggle.busy} onClick={() => void flip(p)}>
                      {p.isActive ? t("switchOff") : t("switchOn")}
                    </Button>
                  </Inline>
                ),
              },
            ]}
          />
        )}
        {!draft && <Button onClick={() => { setNote(""); setDraft(blank); }}>{t("newPack")}</Button>}
      </Card>
      {draft && (
        <Card title={draft.isNew ? t("newPack") : t("editing", { name: draft.name })}>
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <Inline>
                {draft.isNew
                  ? <TextField label={t("code")} hint={t("codeHint")} value={draft.code} onChange={(e) => change({ code: e.target.value })} required />
                  : <p>{draft.code}</p>}
                <TextField label={t("name")} value={draft.name} onChange={(e) => change({ name: e.target.value })} required />
                <TextField label={t("slip")} value={draft.slip} onChange={(e) => change({ slip: e.target.value })} required />
              </Inline>
              <TextField label={t("description")} value={draft.description} onChange={(e) => change({ description: e.target.value })} />
              <fieldset>
                <legend>{t("featuresLegend")}</legend>
                <Stack>
                  {featureFlags.map((f) => (
                    <div key={f}>
                      <CheckField label={f} checked={Boolean(draft.on[f])} onChange={(e) => change({ on: { ...draft.on, [f]: e.target.checked } })} />
                      {draft.on[f] && (
                        <TextAreaField
                          label={t("settingsFor", { flag: f })}
                          hint={f === "price_embedded_barcodes" ? t("settingsHint") : undefined}
                          value={draft.settings[f] ?? ""}
                          onChange={(e) => change({ settings: { ...draft.settings, [f]: e.target.value } })}
                        />
                      )}
                    </div>
                  ))}
                </Stack>
              </fieldset>
              {save.error && <Notice kind="danger">{save.error.message}</Notice>}
              <Inline>
                <Button type="submit" disabled={save.busy || draft.name.trim() === "" || (draft.isNew && draft.code.trim() === "")}>{draft.isNew ? t("create") : t("save")}</Button>
                <Button variant="quiet" onClick={() => { save.clear(); setDraft(null); }}>{t("cancel")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}
    </Stack>
  );
}
