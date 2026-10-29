import { newId } from "@tillknowit/domain";
import { formatDateTime, formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import type { TillCaller } from "../selling-common/TillCaller";
import { messages } from "./messages";

export interface Customer { id: string; name: string; phone: string; email: string | null; messageConsent: boolean; consentAt: string | null; points: number }
interface Points {
  customerId: string; points: number; valueCents: number; minRedeemPoints: number;
  recent: { type: string; points: number; note: string | null; at: string; saleId: string | null }[];
}

const CONSENT_SOURCE = "back_office";

export function Customers({ caller }: { caller: TillCaller }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [search, setSearch] = useState("");
  const [results, setResults] = useState<Customer[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const query = { deviceId: caller.deviceId, staffId: caller.staffId };
  const find = useAction((text: string) => api.get<Customer[]>(`/shops/${shopId}/customers`, { search: text, ...query }));

  async function runFind(text = search) {
    if (text.trim() === "") return;
    const r = await find.run(text.trim());
    if (r.ok) setResults(r.value);
  }

  const current = results?.find((c) => c.id === selected) ?? null;
  const replace = (c: Customer) => setResults((rs) => (rs ?? []).map((x) => (x.id === c.id ? c : x)));

  return (
    <Stack>
      <Card title={t("findHeading")}>
        <Stack>
          <form onSubmit={(e) => { e.preventDefault(); setNote(""); void runFind(); }}>
            <Inline>
              <TextField label={t("findLabel")} value={search} onChange={(e) => setSearch(e.target.value)} />
              <Button type="submit" disabled={find.busy || !caller.ready || search.trim() === ""}>{t("find")}</Button>
            </Inline>
          </form>
          {find.error && <Notice kind="danger">{find.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {results === null ? <p>{t("findFirst")}</p> : (
            <Table
              rows={results}
              rowKey={(c) => c.id}
              empty={t("findEmpty")}
              columns={[
                { header: t("colName"), cell: (c) => c.name },
                { header: t("colPhone"), cell: (c) => c.phone },
                { header: t("colEmail"), cell: (c) => c.email ?? "" },
                { header: t("colConsent"), cell: (c) => <Badge>{c.messageConsent ? t("yes") : t("no")}</Badge> },
                { header: t("colPoints"), cell: (c) => c.points, align: "right" },
                { header: t("colActions"), cell: (c) => <Button variant="secondary" onClick={() => { setNote(""); setSelected(c.id); }}>{t("open", { name: c.name })}</Button> },
              ]}
            />
          )}
        </Stack>
      </Card>
      {current && (
        <CustomerPanel
          key={current.id}
          customer={current}
          others={(results ?? []).filter((c) => c.id !== current.id)}
          caller={caller}
          onChanged={replace}
          onMerged={(msg) => { setNote(msg); setSelected(null); void runFind(); }}
          onClose={() => setSelected(null)}
        />
      )}
      <AddCustomer caller={caller} onAdded={(c) => { setNote(t("added", { name: c.name })); setSearch(c.phone); setResults([c]); }} />
    </Stack>
  );
}

function AddCustomer({ caller, onAdded }: { caller: TillCaller; onAdded: (c: Customer) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [consent, setConsent] = useState(false);
  const add = useAction(() => api.post<Customer>(`/shops/${shopId}/customers`, {
    deviceId: caller.deviceId, staffId: caller.staffId, id: newId(), name: name.trim(), phone: phone.trim(), email: email.trim() === "" ? null : email.trim(),
    messageConsent: consent, consentSource: consent ? CONSENT_SOURCE : null,
  }));

  async function submit() {
    const r = await add.run();
    if (r.ok) { onAdded(r.value); setName(""); setPhone(""); setEmail(""); setConsent(false); }
  }

  return (
    <Card title={t("addHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("name")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required />
            <TextField label={t("email")} type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </Inline>
          <CheckField label={t("consent")} checked={consent} onChange={(e) => setConsent(e.target.checked)} />
          <p>{t("consentHint")}</p>
          {add.error && <Notice kind="danger">{add.error.message}</Notice>}
          <div><Button type="submit" disabled={add.busy || !caller.ready || name.trim() === "" || phone.trim() === ""}>{t("add")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

function CustomerPanel({ customer, others, caller, onChanged, onMerged, onClose }: {
  customer: Customer; others: Customer[]; caller: TillCaller; onChanged: (c: Customer) => void; onMerged: (message: string) => void; onClose: () => void;
}) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const base = `/shops/${shopId}/customers/${customer.id}`;
  const till = { deviceId: caller.deviceId, staffId: caller.staffId };
  const [name, setName] = useState(customer.name);
  const [phone, setPhone] = useState(customer.phone);
  const [email, setEmail] = useState(customer.email ?? "");
  const [consent, setConsent] = useState(customer.messageConsent);
  const [note, setNote] = useState("");
  const points = useLoad(() => api.get<Points>(`${base}/points`, till), [base, caller.deviceId, caller.staffId]);
  const [redeemPoints, setRedeemPoints] = useState("");
  const [adjustPoints, setAdjustPoints] = useState("");
  const [reason, setReason] = useState("");
  const [manager, setManager] = useState("");
  const [source, setSource] = useState("");

  const save = useAction(() => api.put<Customer>(base, { ...till, name: name.trim(), phone: phone.trim(), email: email.trim() === "" ? null : email.trim(), messageConsent: consent, consentSource: consent ? CONSENT_SOURCE : null }));
  const redeem = useAction(() => api.post<{ redemptionReference: string; points: number; valueCents: number; balanceAfter: number }>(`${base}/points/redeem`, { ...till, points: Number(redeemPoints), reference: newId() }));
  const adjust = useAction(() => api.post<unknown>(`${base}/points/adjust`, { deviceId: caller.deviceId, approvedBy: manager.trim(), staffId: caller.staffId, points: Number(adjustPoints), reason: reason.trim() }));
  const merge = useAction(() => api.post<Customer>(`${base}/merge`, { callerPersonId: personId, sourceCustomerId: source }));

  async function doSave() {
    setNote("");
    const r = await save.run();
    if (r.ok) { onChanged(r.value); setNote(t("saved", { name: r.value.name })); }
  }

  async function doRedeem() {
    setNote("");
    const r = await redeem.run();
    if (r.ok) { setNote(t("redeemed", { points: r.value.points, value: formatRand(r.value.valueCents), reference: r.value.redemptionReference, balance: r.value.balanceAfter })); setRedeemPoints(""); points.reload(); onChanged({ ...customer, points: r.value.balanceAfter }); }
  }

  async function doAdjust() {
    setNote("");
    const r = await adjust.run();
    if (r.ok) { setNote(t("adjusted")); setAdjustPoints(""); setReason(""); points.reload(); }
  }

  async function doMerge() {
    const r = await merge.run();
    if (r.ok) onMerged(t("merged", { source: others.find((o) => o.id === source)?.name ?? "", name: customer.name }));
  }

  return (
    <Card title={t("editHeading", { name: customer.name })}>
      <Stack>
        {note && <Notice kind="success">{note}</Notice>}
        <form onSubmit={(e) => { e.preventDefault(); void doSave(); }}>
          <Stack>
            <Inline>
              <TextField label={t("name")} value={name} onChange={(e) => setName(e.target.value)} required />
              <TextField label={t("phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required />
              <TextField label={t("email")} type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
            </Inline>
            <CheckField label={t("consent")} checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            {save.error && <Notice kind="danger">{save.error.message}</Notice>}
            <Inline>
              <Button type="submit" disabled={save.busy || name.trim() === "" || phone.trim() === ""}>{t("save")}</Button>
              <Button variant="quiet" onClick={onClose}>{t("close")}</Button>
            </Inline>
          </Stack>
        </form>

        <h3>{t("pointsHeading")}</h3>
        {points.error && <Notice kind="danger">{points.error.message}</Notice>}
        {points.loading && !points.data ? <p>{t("loading")}</p> : points.data && (
          <Stack>
            <p>{t("pointsBalance", { points: points.data.points, value: formatRand(points.data.valueCents) })} {t("pointsMin", { points: points.data.minRedeemPoints })}</p>
            <form onSubmit={(e) => { e.preventDefault(); void doRedeem(); }}>
              <Inline>
                <TextField label={t("redeemPoints")} type="number" min={1} value={redeemPoints} onChange={(e) => setRedeemPoints(e.target.value)} />
                <Button type="submit" disabled={redeem.busy || !(Number(redeemPoints) >= 1)}>{t("redeem")}</Button>
              </Inline>
            </form>
            {redeem.error && <Notice kind="danger">{redeem.error.message}</Notice>}
            <form onSubmit={(e) => { e.preventDefault(); void doAdjust(); }}>
              <Inline>
                <TextField label={t("adjustPoints")} type="number" value={adjustPoints} onChange={(e) => setAdjustPoints(e.target.value)} />
                <TextField label={t("adjustReason")} value={reason} onChange={(e) => setReason(e.target.value)} />
                <TextField label={t("managerId")} value={manager} onChange={(e) => setManager(e.target.value)} />
                <Button type="submit" disabled={adjust.busy || !(Number(adjustPoints) !== 0) || reason.trim() === "" || manager.trim() === ""}>{t("adjust")}</Button>
              </Inline>
            </form>
            {adjust.error && <Notice kind="danger">{adjust.error.message}</Notice>}
            <h4>{t("historyHeading")}</h4>
            <Table
              rows={points.data.recent}
              rowKey={(e) => `${e.at}-${e.type}-${e.points}-${e.note ?? ""}`}
              empty={t("historyEmpty")}
              columns={[
                { header: t("colWhen"), cell: (e) => formatDateTime(e.at) },
                { header: t("colType"), cell: (e) => e.type },
                { header: t("colChange"), cell: (e) => e.points, align: "right" },
                { header: t("colNote"), cell: (e) => e.note ?? "" },
              ]}
            />
          </Stack>
        )}

        <h3>{t("mergeHeading")}</h3>
        <p>{t("mergeHint", { name: customer.name })}</p>
        {others.length === 0 ? <p>{t("mergeNone")}</p> : (
          <Inline>
            <SelectField label={t("mergeSource")} value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="">{t("mergeChoose")}</option>
              {others.map((o) => <option key={o.id} value={o.id}>{`${o.name} (${o.phone})`}</option>)}
            </SelectField>
            <Button variant="secondary" disabled={merge.busy || source === ""} onClick={() => void doMerge()}>{t("merge", { name: customer.name })}</Button>
          </Inline>
        )}
        {merge.error && <Notice kind="danger">{merge.error.message}</Notice>}
      </Stack>
    </Card>
  );
}
