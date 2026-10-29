import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, CheckField, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { dayCodes } from "../../shared/days";
import { messages } from "./messages";

interface AreaEntry { municipalityCode: string; days: string[] }
interface Supplier { id: string; name: string; minOrderCents: number; deliveryFeeCents: number; paymentTerms: string; areas: AreaEntry[] }

function useSupplier(supplierId: string) {
  const api = useApi();
  const { personId } = useSession();
  return useLoad(() => api.get<Supplier>(`/suppliers/${supplierId}`, { callerPersonId: personId }), [supplierId, personId]);
}

export function Areas({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const supplier = useSupplier(supplierId);
  const [areas, setAreas] = useState<AreaEntry[]>([]);
  const [saved, setSaved] = useState(false);
  useEffect(() => { if (supplier.data) setAreas(supplier.data.areas.map((a) => ({ ...a, days: [...a.days] }))); }, [supplier.data]);
  const save = useAction(() => api.put<void>(`/suppliers/${supplierId}/areas`, { callerPersonId: personId, areas: areas.filter((a) => a.municipalityCode.trim() !== "").map((a) => ({ municipalityCode: a.municipalityCode.trim(), days: a.days })) }));
  const setArea = (i: number, patch: Partial<AreaEntry>) => { setSaved(false); setAreas(areas.map((a, n) => (n === i ? { ...a, ...patch } : a))); };

  return (
    <Card title={t("areasHeading")}>
      {supplier.error && <Notice kind="danger">{supplier.error.message}</Notice>}
      {supplier.loading && !supplier.data ? <p>{t("loading")}</p> : (
        <form onSubmit={(e) => { e.preventDefault(); void save.run().then((r) => setSaved(r.ok)); }}>
          <Stack>
            {areas.length === 0 && <p>{t("noAreaYet")}</p>}
            {areas.map((a, i) => (
              <fieldset key={i}>
                <legend>{t("days")}</legend>
                <Inline>
                  <TextField label={t("areaCode")} value={a.municipalityCode} onChange={(e) => setArea(i, { municipalityCode: e.target.value })} />
                  {dayCodes.map((d) => (
                    <CheckField key={d} label={d} checked={a.days.includes(d)} onChange={(e) => setArea(i, { days: e.target.checked ? [...a.days, d] : a.days.filter((x) => x !== d) })} />
                  ))}
                  <Button variant="quiet" aria-label={t("removeArea", { n: i + 1 })} onClick={() => { setSaved(false); setAreas(areas.filter((_, n) => n !== i)); }}>x</Button>
                </Inline>
              </fieldset>
            ))}
            <Inline>
              <Button variant="secondary" onClick={() => setAreas([...areas, { municipalityCode: "", days: [] }])}>{t("addArea")}</Button>
              <Button type="submit" disabled={save.busy}>{t("save")}</Button>
            </Inline>
            {save.error && <Notice kind="danger">{save.error.message}</Notice>}
            {saved && <Notice kind="success">{t("saved")}</Notice>}
          </Stack>
        </form>
      )}
    </Card>
  );
}

export function Terms({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const supplier = useSupplier(supplierId);
  const [min, setMin] = useState("");
  const [fee, setFee] = useState("");
  const [terms, setTerms] = useState("both");
  const [problem, setProblem] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    if (!supplier.data) return;
    setMin((supplier.data.minOrderCents / 100).toFixed(2));
    setFee((supplier.data.deliveryFeeCents / 100).toFixed(2));
    setTerms(supplier.data.paymentTerms);
  }, [supplier.data]);
  const save = useAction((minOrderCents: number, deliveryFeeCents: number) => api.put<void>(`/suppliers/${supplierId}/terms`, { callerPersonId: personId, minOrderCents, deliveryFeeCents, paymentTerms: terms }));

  async function submit() {
    const m = parseRandToCents(min);
    const f = parseRandToCents(fee);
    setSaved(false);
    if (m === null || f === null) { setProblem(t("badMoney")); return; }
    setProblem("");
    const r = await save.run(m, f);
    setSaved(r.ok);
  }

  return (
    <Card title={t("termsHeading")}>
      {supplier.error && <Notice kind="danger">{supplier.error.message}</Notice>}
      {supplier.loading && !supplier.data ? <p>{t("loading")}</p> : (
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            {supplier.data && <p>{formatRand(supplier.data.minOrderCents)} / {formatRand(supplier.data.deliveryFeeCents)}</p>}
            <Inline>
              <TextField label={t("minOrder")} inputMode="decimal" value={min} onChange={(e) => setMin(e.target.value)} />
              <TextField label={t("deliveryFee")} inputMode="decimal" value={fee} onChange={(e) => setFee(e.target.value)} />
              <SelectField label={t("paymentTerms")} value={terms} onChange={(e) => setTerms(e.target.value)}>
                <option value="on_delivery">{t("onDelivery")}</option>
                <option value="in_app">{t("inApp")}</option>
                <option value="both">{t("both")}</option>
              </SelectField>
              <Button type="submit" disabled={save.busy}>{t("save")}</Button>
            </Inline>
            {problem && <Notice kind="danger">{problem}</Notice>}
            {save.error && <Notice kind="danger">{save.error.message}</Notice>}
            {saved && <Notice kind="success">{t("saved")}</Notice>}
          </Stack>
        </form>
      )}
    </Card>
  );
}

export function Reps({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [done, setDone] = useState("");
  const invite = useAction(() => api.post<{ membershipId: string }>(`/suppliers/${supplierId}/reps`, { callerPersonId: personId, name: name.trim(), phone: phone.trim() }));

  async function submit() {
    setDone("");
    const r = await invite.run();
    if (r.ok) { setDone(t("invited", { name: name.trim() })); setName(""); setPhone(""); }
  }

  return (
    <Card title={t("repsHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("repsIntro")}</p>
          <Inline>
            <TextField label={t("repName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("repPhone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required />
            <Button type="submit" disabled={invite.busy || name.trim() === "" || phone.trim() === ""}>{t("invite")}</Button>
          </Inline>
          {invite.error && <Notice kind="danger">{invite.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}
