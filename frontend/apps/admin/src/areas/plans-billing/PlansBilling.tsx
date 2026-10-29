import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface PlanFeature {
  feature: string;
  limit: number | null;
}

export interface Plan {
  id: string;
  code: string;
  audience: "shop" | "supplier";
  name: string;
  priceCents: number;
  isActive: boolean;
  features: PlanFeature[];
}

/** The feature codes plans can switch on (Billing.Contracts PlanFeatures). Features already on a plan are kept even if not listed here. */
export const knownFeatures = ["messages", "multi_branch", "accounting_export", "insight_reports", "pay_later", "partner_api", "sponsored", "supplier_analytics"] as const;

interface Draft {
  isNew: boolean;
  code: string;
  audience: "shop" | "supplier";
  name: string;
  price: string;
  isActive: boolean;
  codes: string[];
  on: Record<string, boolean>;
  limits: Record<string, string>;
}

const blank: Draft = { isNew: true, code: "", audience: "shop", name: "", price: "0", isActive: true, codes: [...knownFeatures], on: {}, limits: {} };

function toDraft(p: Plan): Draft {
  const own = p.features.map((f) => f.feature);
  return {
    isNew: false, code: p.code, audience: p.audience, name: p.name, price: String(p.priceCents / 100), isActive: p.isActive,
    codes: [...knownFeatures, ...own.filter((f) => !(knownFeatures as readonly string[]).includes(f))],
    on: Object.fromEntries(own.map((f) => [f, true])),
    limits: Object.fromEntries(p.features.map((f) => [f.feature, f.limit === null ? "" : String(f.limit)])),
  };
}

export function PlansBilling() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [audience, setAudience] = useState("");
  const list = useLoad(() => api.get<Plan[]>("/plans", { audience }), [audience]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [note, setNote] = useState("");
  const [problem, setProblem] = useState<{ price?: string; limit?: string }>({});

  const save = useAction((body: unknown) => api.put<Plan>("/plans", body));

  async function submit() {
    const d = draft!;
    const price = parseRandToCents(d.price);
    const bad: typeof problem = {};
    if (price === null) bad.price = t("priceInvalid");
    const features: PlanFeature[] = [];
    for (const code of d.codes.filter((c) => d.on[c])) {
      const text = (d.limits[code] ?? "").trim();
      if (text !== "" && !/^\d+$/.test(text)) { bad.limit = t("limitInvalid"); break; }
      features.push({ feature: code, limit: text === "" ? null : Number(text) });
    }
    setProblem(bad);
    if (bad.price || bad.limit || price === null) return;
    const r = await save.run({ callerPersonId: personId, code: d.code.trim(), audience: d.audience, name: d.name, priceCents: price, isActive: d.isActive, features });
    if (r.ok) {
      setNote(t("saved", { name: r.value.name }));
      setDraft(null);
      list.reload();
    }
  }

  const change = (patch: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...patch } : d));

  return (
    <Stack>
      <Card title={t("heading")}>
        <Notice kind="warning">{t("placeholderNotice")}</Notice>
        <p>{t("hiddenNote")}</p>
        <SelectField label={t("filter")} value={audience} onChange={(e) => setAudience(e.target.value)}>
          <option value="">{t("all")}</option>
          <option value="shop">{t("shop")}</option>
          <option value="supplier">{t("supplier")}</option>
        </SelectField>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(p) => p.code}
            empty={t("empty")}
            columns={[
              { header: t("colName"), cell: (p) => p.name },
              { header: t("colCode"), cell: (p) => p.code },
              { header: t("colFor"), cell: (p) => <Badge>{t(p.audience)}</Badge> },
              { header: t("colPrice"), cell: (p) => (p.priceCents === 0 ? t("free") : formatRand(p.priceCents)), align: "right" },
              {
                header: t("colFeatures"),
                cell: (p) => (p.features.length === 0 ? t("noFeatures") : p.features.map((f) => (f.limit === null ? f.feature : t("limitOf", { feature: f.feature, limit: f.limit }))).join(", ")),
              },
              { header: t("colActions"), cell: (p) => <Button variant="secondary" aria-label={`${t("edit")} ${p.name} (${p.code})`} onClick={() => { setNote(""); setProblem({}); setDraft(toDraft(p)); }}>{t("edit")}</Button> },
            ]}
          />
        )}
        {!draft && <Button onClick={() => { setNote(""); setProblem({}); setDraft(blank); }}>{t("newPlan")}</Button>}
      </Card>
      {draft && (
        <Card title={draft.isNew ? t("newPlan") : t("editing", { name: draft.name })}>
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <Inline>
                {draft.isNew ? (
                  <>
                    <TextField label={t("code")} hint={t("codeHint")} value={draft.code} onChange={(e) => change({ code: e.target.value })} required />
                    <SelectField label={t("audience")} value={draft.audience} onChange={(e) => change({ audience: e.target.value as Draft["audience"] })}>
                      <option value="shop">{t("shop")}</option>
                      <option value="supplier">{t("supplier")}</option>
                    </SelectField>
                  </>
                ) : <p>{draft.code} ({t(draft.audience)})</p>}
                <TextField label={t("name")} value={draft.name} onChange={(e) => change({ name: e.target.value })} required />
                <TextField label={t("price")} inputMode="decimal" value={draft.price} error={problem.price ?? null} onChange={(e) => change({ price: e.target.value })} required />
              </Inline>
              <CheckField label={t("isActive")} checked={draft.isActive} onChange={(e) => change({ isActive: e.target.checked })} />
              <fieldset>
                <legend>{t("featuresLegend")}</legend>
                <Stack>
                  {draft.codes.map((c) => (
                    <Inline key={c}>
                      <CheckField label={c} checked={Boolean(draft.on[c])} onChange={(e) => change({ on: { ...draft.on, [c]: e.target.checked } })} />
                      {draft.on[c] && (
                        <TextField label={t("limitFor", { feature: c })} hint={t("limitHint")} inputMode="numeric" value={draft.limits[c] ?? ""}
                          onChange={(e) => change({ limits: { ...draft.limits, [c]: e.target.value } })} />
                      )}
                    </Inline>
                  ))}
                </Stack>
                {problem.limit && <Notice kind="danger">{problem.limit}</Notice>}
              </fieldset>
              {save.error && <Notice kind="danger">{save.error.message}</Notice>}
              <Inline>
                <Button type="submit" disabled={save.busy || draft.name.trim() === "" || (draft.isNew && draft.code.trim() === "")}>{t("save")}</Button>
                <Button variant="quiet" onClick={() => { save.clear(); setDraft(null); }}>{t("cancel")}</Button>
              </Inline>
            </Stack>
          </form>
        </Card>
      )}
    </Stack>
  );
}
