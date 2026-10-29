import { formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type SupplierKey } from "./messages";

export interface PendingSupplier {
  id: string;
  name: string;
  status: string;
  rejectionReason: string | null;
  minOrderCents: number;
  deliveryFeeCents: number;
  paymentTerms: string;
  areas: { municipalityCode: string; days: string[] }[];
  verified: boolean;
}

/** The checklist the server requires (Marketplace.Contracts VerifySupplierRequest). All are needed. */
export const checklistItems = ["registration_checked", "contact_confirmed", "delivery_capability", "prices_reviewed"] as const;

/** The ranking settings, their allowed ranges and the value used when none is saved (Marketplace.Domain/OfferEngine.cs RankingWeights). */
export const settingDefs = [
  { key: "ranking.weight_cost", min: 0, max: 100, fallback: "1" },
  { key: "ranking.weight_rating", min: 0, max: 100, fallback: "0.02" },
  { key: "ranking.weight_delivery", min: 0, max: 100, fallback: "0.01" },
  { key: "ranking.sponsored_boost_percent", min: 0, max: 20, fallback: "0" },
  { key: "ranking.vat_percent", min: 0, max: 30, fallback: "15" },
] as const satisfies ReadonlyArray<{ key: SupplierKey; min: number; max: number; fallback: string }>;

export function Suppliers() {
  return (
    <Stack>
      <Pending />
      <Settings />
    </Stack>
  );
}

function Pending() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<PendingSupplier[]>("/admin/suppliers/pending", { adminPersonId: personId }), [personId]);
  const [open, setOpen] = useState<PendingSupplier | null>(null);
  const [ticked, setTicked] = useState<Record<string, boolean>>({});
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");

  const verify = useAction(() => api.post<void>(`/admin/suppliers/${open!.id}/verify`, { adminPersonId: personId, checklist: checklistItems.filter((c) => ticked[c]) }));
  const reject = useAction(() => api.post<void>(`/admin/suppliers/${open!.id}/reject`, { adminPersonId: personId, reason: reason.trim() }));
  const busy = verify.busy || reject.busy;

  function close() {
    setOpen(null);
    setTicked({});
    setReason("");
    verify.clear();
    reject.clear();
  }

  async function finish(kind: "verify" | "reject") {
    const name = open!.name;
    const r = await (kind === "verify" ? verify.run() : reject.run());
    if (r.ok) {
      setNote(t(kind === "verify" ? "verified" : "rejected", { name }));
      close();
      list.reload();
    }
  }

  return (
    <>
      <Card title={t("pendingHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(s) => s.id}
            empty={t("pendingEmpty")}
            columns={[
              { header: t("colName"), cell: (s) => s.name },
              { header: t("colStatus"), cell: (s) => <Badge>{s.status}</Badge> },
              { header: t("colAreas"), cell: (s) => s.areas.map((a) => a.municipalityCode).join(", ") },
              { header: t("colTerms"), cell: (s) => t("terms", { min: formatRand(s.minOrderCents), fee: formatRand(s.deliveryFeeCents), payment: s.paymentTerms }) },
              { header: t("colActions"), cell: (s) => <Button variant="secondary" aria-label={`${t("review")} ${s.name}`} onClick={() => { close(); setNote(""); setOpen(s); }}>{t("review")}</Button> },
            ]}
          />
        )}
      </Card>
      {open && (
        <Card title={t("reviewing", { name: open.name })}>
          <Stack>
            <fieldset>
              <legend>{t("checklistLegend")}</legend>
              <Stack>
                {checklistItems.map((c) => <CheckField key={c} label={t(c)} checked={Boolean(ticked[c])} onChange={(e) => setTicked({ ...ticked, [c]: e.target.checked })} />)}
              </Stack>
            </fieldset>
            <Inline>
              <Button disabled={busy} onClick={() => void finish("verify")}>{t("verify")}</Button>
            </Inline>
            {verify.error && <Notice kind="danger">{verify.error.message}</Notice>}
            <TextField label={t("reason")} hint={t("reasonHint")} value={reason} onChange={(e) => setReason(e.target.value)} />
            <Inline>
              <Button variant="secondary" disabled={busy || reason.trim() === ""} onClick={() => void finish("reject")}>{t("reject")}</Button>
              <Button variant="quiet" onClick={close}>{t("close")}</Button>
            </Inline>
            {reject.error && <Notice kind="danger">{reject.error.message}</Notice>}
          </Stack>
        </Card>
      )}
    </>
  );
}

function Settings() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Record<string, string>>("/admin/marketplace/settings", { adminPersonId: personId }), [personId]);

  return (
    <Card title={t("settingsHeading")}>
      <p>{t("settingsIntro")}</p>
      {list.error && <Notice kind="danger">{list.error.message}</Notice>}
      {list.loading && !list.data ? <p>{t("loading")}</p> : list.data && (
        <ul>
          {settingDefs.map((d) => <SettingRow key={d.key} def={d} stored={list.data?.[d.key]} />)}
        </ul>
      )}
    </Card>
  );
}

function SettingRow({ def, stored }: { def: (typeof settingDefs)[number]; stored: string | undefined }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [value, setValue] = useState(stored ?? def.fallback);
  const [local, setLocal] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const save = useAction((v: string) => api.put<void>(`/admin/marketplace/settings/${def.key}`, { adminPersonId: personId, value: v }));
  const name = t(def.key);
  const range = t("range", { min: def.min, max: def.max });

  async function submit() {
    const n = Number(value.trim());
    setSaved(false);
    if (value.trim() === "" || !Number.isFinite(n)) return setLocal(t("notNumber"));
    // The server owns the rule; this only avoids a round trip for an answer we already know.
    if (n < def.min || n > def.max) return setLocal(t("outOfRange", { min: def.min, max: def.max }));
    setLocal(null);
    const r = await save.run(value.trim());
    if (r.ok) {
      setSaved(true);
    }
  }

  return (
    <li>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Inline>
          <TextField label={name} hint={`${t("colRange")}: ${range}. ${t("colDefault")}: ${def.fallback}`} inputMode="decimal" value={value} error={local ?? save.error?.message ?? null}
            onChange={(e) => { setValue(e.target.value); setSaved(false); }} />
          <Button type="submit" aria-label={t("saveSetting", { name })} disabled={save.busy}>{t("save")}</Button>
        </Inline>
        {saved && <Notice kind="success">{t("settingSaved", { name })}</Notice>}
      </form>
    </li>
  );
}
