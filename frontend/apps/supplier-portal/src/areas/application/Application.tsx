import { formatRand, useAction, useApi, useLoad, useMessages, useSession, useSetSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { dayCodes } from "../../shared/days";
import { readSupplierId, saveSupplierId } from "../../shared/SupplierGate";
import { messages } from "./messages";


export interface AreaEntry { municipalityCode: string; days: string[] }
export interface Supplier {
  id: string; name: string; status: string; rejectionReason: string | null; minOrderCents: number; deliveryFeeCents: number; paymentTerms: string;
  areas: AreaEntry[]; verified: boolean;
}
interface Applied { supplierId: string; organisationId: string; status: string }

function SupplierDetails({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const s = useLoad(() => api.get<Supplier>(`/suppliers/${supplierId}`, { callerPersonId: personId }), [supplierId, personId]);
  if (s.error) return <Notice kind="danger">{s.error.message}</Notice>;
  if (s.loading || !s.data) return <p>{t("loading")}</p>;
  const d = s.data;
  return (
    <Stack>
      <Inline><strong>{d.name}</strong><Badge>{d.status}</Badge></Inline>
      <p>{t("verified")}: {d.verified ? t("yes") : t("no")}</p>
      {d.rejectionReason && <Notice kind="warning">{t("rejection", { reason: d.rejectionReason })}</Notice>}
      <p>{t("minOrder")}: {formatRand(d.minOrderCents)} | {t("deliveryFee")}: {formatRand(d.deliveryFeeCents)} | {t("paymentTerms")}: {d.paymentTerms}</p>
      <h3>{t("areas")}</h3>
      {d.areas.length === 0 ? <p>{t("noAreas")}</p> : <ul>{d.areas.map((a) => <li key={a.municipalityCode}>{a.municipalityCode}: {a.days.join(", ")}</li>)}</ul>}
    </Stack>
  );
}

export function Application() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const setSession = useSetSession();
  const [supplierId, setSupplierId] = useState(readSupplierId);
  const [draftId, setDraftId] = useState("");
  const [form, setForm] = useState({ name: "", registrationNo: "", contactName: "", phone: "", whatsapp: "", email: "" });
  const [areas, setAreas] = useState<AreaEntry[]>([{ municipalityCode: "", days: [] }]);
  const [done, setDone] = useState("");
  const [localError, setLocalError] = useState("");
  const blank = (v: string) => (v.trim() === "" ? null : v.trim());

  const apply = useAction(() => api.post<Applied>("/suppliers/applications", {
    applicantPersonId: personId, name: form.name.trim(), registrationNo: blank(form.registrationNo), contactName: form.contactName.trim(),
    phone: blank(form.phone), whatsapp: blank(form.whatsapp), email: blank(form.email),
    areas: areas.filter((a) => a.municipalityCode.trim() !== "").map((a) => ({ municipalityCode: a.municipalityCode.trim(), days: a.days })),
  }));

  async function submit() {
    setDone("");
    if (!areas.some((a) => a.municipalityCode.trim() !== "" && a.days.length > 0)) { setLocalError(t("needArea")); return; }
    setLocalError("");
    const r = await apply.run();
    if (r.ok) {
      saveSupplierId(r.value.supplierId);
      setSupplierId(r.value.supplierId);
      setSession({ supplierOrganisationId: r.value.organisationId });
      setDone(t("applied", { status: r.value.status, id: r.value.supplierId }));
    }
  }

  const setArea = (i: number, patch: Partial<AreaEntry>) => setAreas(areas.map((a, n) => (n === i ? { ...a, ...patch } : a)));

  return (
    <Stack>
      <Card title={t("detailsHeading")}>
        {supplierId === "" ? <p>{t("noSupplier")}</p> : <SupplierDetails supplierId={supplierId} />}
        <form onSubmit={(e) => { e.preventDefault(); const v = draftId.trim(); if (v) { saveSupplierId(v); setSupplierId(v); } }}>
          <Inline>
            <TextField label={t("supplierId")} hint={t("supplierIdHint")} value={draftId} onChange={(e) => setDraftId(e.target.value)} />
            <Button type="submit" variant="secondary" disabled={draftId.trim() === ""}>{t("look")}</Button>
          </Inline>
        </form>
      </Card>
      <Card title={t("applyHeading")}>
        <p>{t("applyIntro")}</p>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <Inline>
              <TextField label={t("bizName")} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
              <TextField label={t("registrationNo")} value={form.registrationNo} onChange={(e) => setForm({ ...form, registrationNo: e.target.value })} />
              <TextField label={t("contactName")} value={form.contactName} onChange={(e) => setForm({ ...form, contactName: e.target.value })} required />
            </Inline>
            <Inline>
              <TextField label={t("phone")} value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
              <TextField label={t("whatsapp")} value={form.whatsapp} onChange={(e) => setForm({ ...form, whatsapp: e.target.value })} />
              <TextField label={t("email")} type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            </Inline>
            {areas.map((a, i) => (
              <fieldset key={i}>
                <legend>{t("days")}</legend>
                <Inline>
                  <TextField label={t("areaCode")} hint={t("areaCodeHint")} value={a.municipalityCode} onChange={(e) => setArea(i, { municipalityCode: e.target.value })} />
                  {dayCodes.map((d) => (
                    <CheckField key={d} label={d} checked={a.days.includes(d)} onChange={(e) => setArea(i, { days: e.target.checked ? [...a.days, d] : a.days.filter((x) => x !== d) })} />
                  ))}
                  {areas.length > 1 && <Button variant="quiet" aria-label={t("removeArea", { n: i + 1 })} onClick={() => setAreas(areas.filter((_, n) => n !== i))}>x</Button>}
                </Inline>
              </fieldset>
            ))}
            <Inline>
              <Button variant="secondary" onClick={() => setAreas([...areas, { municipalityCode: "", days: [] }])}>{t("addArea")}</Button>
              <Button type="submit" disabled={apply.busy || form.name.trim() === "" || form.contactName.trim() === ""}>{t("submit")}</Button>
            </Inline>
            {localError && <Notice kind="danger">{localError}</Notice>}
            {apply.error && <Notice kind="danger">{apply.error.message}</Notice>}
            {done && <Notice kind="success">{done}</Notice>}
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}
