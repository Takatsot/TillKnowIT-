import { formatDate, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { ProductChecks, ProductSelect, useProducts, type ProductRef } from "../selling-common/products";
import { useTillCaller } from "../selling-common/TillCaller";
import { messages, type PromotionsKey } from "./messages";

export interface Promotion {
  id: string;
  name: string;
  type: string;
  productIds: string[];
  percent: number;
  amountCents: number;
  items: { productId: string; quantity: number }[];
  startsAt: string;
  endsAt: string;
  days: string[];
  from: string | null;
  to: string | null;
  isActive: boolean;
}

interface PricedBasket {
  subtotalCents: number;
  discountCents: number;
  totalCents: number;
  applied: { promotionId: string; name: string; discountCents: number; times: number }[];
}

const types = ["percent_off", "amount_off", "fixed_price", "combo"] as const;
const weekdays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

interface Draft {
  name: string;
  type: string;
  percent: string;
  amount: string;
  productIds: string[];
  items: { productId: string; quantity: string }[];
  starts: string;
  ends: string;
  days: string[];
  from: string;
  to: string;
}

const blank: Draft = { name: "", type: "percent_off", percent: "10", amount: "", productIds: [], items: [], starts: "", ends: "", days: [], from: "", to: "" };

const toDraft = (p: Promotion): Draft => ({
  name: p.name, type: p.type, percent: String(p.percent), amount: p.amountCents === 0 ? "" : (p.amountCents / 100).toFixed(2), productIds: p.productIds,
  items: p.items.map((i) => ({ productId: i.productId, quantity: String(i.quantity) })), starts: formatDate(p.startsAt), ends: formatDate(p.endsAt),
  days: p.days, from: p.from ?? "", to: p.to ?? "",
});

export function Promotions() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const caller = useTillCaller();
  const products = useProducts();
  const list = useLoad(
    () => (caller.ready ? api.get<Promotion[]>(`/shops/${shopId}/promotions`, { deviceId: caller.deviceId, staffId: caller.staffId }) : Promise.resolve({ ok: true as const, value: [] as Promotion[] })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const [editing, setEditing] = useState<Promotion | null>(null);
  const [note, setNote] = useState("");
  const names = new Map((products.data ?? []).map((p) => [p.productId, p.name]));

  const setActive = useAction((p: Promotion, active: boolean) => api.post<Promotion>(`/shops/${shopId}/promotions/${p.id}/active`, { callerPersonId: personId, active }));
  const toggle = async (p: Promotion) => {
    setNote("");
    const r = await setActive.run(p, !p.isActive);
    if (r.ok) { setNote(t("statusChanged", { name: p.name, state: r.value.isActive ? t("on") : t("off") })); list.reload(); }
  };

  const deal = (p: Promotion) =>
    p.type === "percent_off" ? t("dealPercent", { percent: p.percent })
      : p.type === "amount_off" ? t("dealAmount", { amount: formatRand(p.amountCents) })
        : p.type === "fixed_price" ? t("dealFixed", { amount: formatRand(p.amountCents) })
          : t("dealCombo", { items: p.items.reduce((n, i) => n + i.quantity, 0), amount: formatRand(p.amountCents) });

  const when = (p: Promotion) => [
    t("between", { from: formatDate(p.startsAt), to: formatDate(p.endsAt) }),
    p.days.length > 0 && p.days.length < 7 ? t("days", { days: p.days.map((d) => t(d as PromotionsKey)).join(", ") }) : "",
    p.from && p.to ? t("hours", { from: p.from, to: p.to }) : "",
  ].filter(Boolean).join(". ");

  return (
    <Stack>
      {caller.card}
      <Card title={t("listHeading")}>
        <Stack>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {setActive.error && <Notice kind="danger">{setActive.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(p) => p.id}
              empty={t("empty")}
              columns={[
                { header: t("colName"), cell: (p) => p.name },
                { header: t("colType"), cell: (p) => t(`type_${p.type}` as PromotionsKey) },
                { header: t("colDeal"), cell: deal },
                { header: t("colWhen"), cell: when },
                { header: t("colStatus"), cell: (p) => <Badge>{p.isActive ? t("on") : t("off")}</Badge> },
                {
                  header: t("colActions"),
                  cell: (p) => (
                    <Inline>
                      <Button variant="secondary" onClick={() => { setNote(""); setEditing(p); }}>{t("edit", { name: p.name })}</Button>
                      <Button variant="quiet" disabled={setActive.busy} onClick={() => void toggle(p)}>{p.isActive ? t("turnOff", { name: p.name }) : t("turnOn", { name: p.name })}</Button>
                    </Inline>
                  ),
                },
              ]}
            />
          )}
          {list.data && list.data.length > 0 && names.size === 0 && !products.loading && <Notice kind="info">{t("noProducts")}</Notice>}
        </Stack>
      </Card>
      <PromotionForm
        key={editing?.id ?? "new"}
        editing={editing}
        products={products.data ?? []}
        callerPersonId={personId}
        onSaved={(p) => { setNote(t("saved", { name: p.name })); setEditing(null); list.reload(); }}
        onCancel={() => setEditing(null)}
      />
      <BasketPreview products={products.data ?? []} deviceId={caller.deviceId} staffId={caller.staffId} ready={caller.ready} />
    </Stack>
  );
}

function PromotionForm({ editing, products, callerPersonId, onSaved, onCancel }: {
  editing: Promotion | null; products: readonly ProductRef[]; callerPersonId: string; onSaved: (p: Promotion) => void; onCancel: () => void;
}) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [d, setD] = useState<Draft>(editing ? toDraft(editing) : blank);
  const [problem, setProblem] = useState<string | null>(null);
  const set = (patch: Partial<Draft>) => setD((x) => ({ ...x, ...patch }));

  const save = useAction((body: unknown) => (editing ? api.patch<Promotion>(`/shops/${shopId}/promotions/${editing.id}`, body) : api.post<Promotion>(`/shops/${shopId}/promotions`, body)));

  async function submit() {
    const needsAmount = d.type !== "percent_off";
    const cents = needsAmount ? parseRandToCents(d.amount) : 0;
    if (cents === null) { setProblem(t("amountInvalid")); return; }
    setProblem(null);
    const body = {
      callerPersonId, name: d.name.trim(), type: d.type,
      productIds: d.type === "combo" ? [] : d.productIds,
      percent: d.type === "percent_off" ? Number(d.percent) : 0,
      amountCents: cents,
      items: d.type === "combo" ? d.items.filter((i) => i.productId !== "").map((i) => ({ productId: i.productId, quantity: Math.max(1, Number(i.quantity) || 1) })) : [],
      startsAt: `${d.starts}T00:00:00+02:00`, endsAt: `${d.ends}T23:59:59+02:00`,
      days: d.days.length === 0 ? null : d.days,
      from: d.from === "" || d.to === "" ? null : d.from, to: d.from === "" || d.to === "" ? null : d.to,
    };
    const r = await save.run(body);
    if (r.ok) { if (!editing) setD(blank); onSaved(r.value); }
  }

  const amountLabel = d.type === "amount_off" ? t("amount") : d.type === "fixed_price" ? t("amountFixed") : t("amountCombo");

  return (
    <Card title={editing ? t("formEdit") : t("formNew")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("name")} value={d.name} onChange={(e) => set({ name: e.target.value })} required />
            <SelectField label={t("type")} value={d.type} onChange={(e) => set({ type: e.target.value })}>
              {types.map((x) => <option key={x} value={x}>{t(`type_${x}` as PromotionsKey)}</option>)}
            </SelectField>
            {d.type === "percent_off"
              ? <TextField label={t("percent")} type="number" min={1} max={100} value={d.percent} onChange={(e) => set({ percent: e.target.value })} required />
              : <TextField label={amountLabel} inputMode="decimal" value={d.amount} error={problem} onChange={(e) => set({ amount: e.target.value })} required />}
          </Inline>
          <Inline>
            <TextField label={t("starts")} type="date" value={d.starts} onChange={(e) => set({ starts: e.target.value })} required />
            <TextField label={t("ends")} type="date" value={d.ends} onChange={(e) => set({ ends: e.target.value })} required />
            <TextField label={t("hoursFrom")} type="time" hint={t("hoursHint")} value={d.from} onChange={(e) => set({ from: e.target.value })} />
            <TextField label={t("hoursTo")} type="time" value={d.to} onChange={(e) => set({ to: e.target.value })} />
          </Inline>
          <fieldset>
            <legend>{t("daysLegend")}</legend>
            <Inline>
              {weekdays.map((day) => (
                <CheckField key={day} label={t(day)} checked={d.days.includes(day)} onChange={(e) => set({ days: e.target.checked ? [...d.days, day] : d.days.filter((x) => x !== day) })} />
              ))}
            </Inline>
          </fieldset>
          {d.type === "combo" ? (
            <fieldset>
              <legend>{t("comboLegend")}</legend>
              <Stack>
                {d.items.map((item, n) => (
                  <Inline key={n}>
                    <ProductSelect label={`${t("comboProduct")} ${n + 1}`} value={item.productId} products={products}
                      onChange={(id) => set({ items: d.items.map((x, i) => (i === n ? { ...x, productId: id } : x)) })} />
                    <TextField label={`${t("comboQuantity")} ${n + 1}`} type="number" min={1} value={item.quantity}
                      onChange={(e) => set({ items: d.items.map((x, i) => (i === n ? { ...x, quantity: e.target.value } : x)) })} />
                    <Button variant="quiet" onClick={() => set({ items: d.items.filter((_, i) => i !== n) })}>{t("comboRemove", { n: n + 1 })}</Button>
                  </Inline>
                ))}
                <div><Button variant="secondary" onClick={() => set({ items: [...d.items, { productId: "", quantity: "1" }] })}>{t("comboAdd")}</Button></div>
              </Stack>
            </fieldset>
          ) : (
            <ProductChecks legend={t("productsLegend")} selected={d.productIds} onChange={(ids) => set({ productIds: ids })} products={products} />
          )}
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          <Inline>
            <Button type="submit" disabled={save.busy || d.name.trim() === "" || d.starts === "" || d.ends === ""}>{t("save")}</Button>
            {editing && <Button variant="quiet" onClick={onCancel}>{t("cancelEdit")}</Button>}
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}

interface PreviewLine { productId: string; quantity: string; price: string }

function BasketPreview({ products, deviceId, staffId, ready }: { products: readonly ProductRef[]; deviceId: string; staffId: string; ready: boolean }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [lines, setLines] = useState<PreviewLine[]>([{ productId: "", quantity: "1", price: "" }]);
  const [problem, setProblem] = useState("");
  const [result, setResult] = useState<PricedBasket | null>(null);
  const run = useAction((body: unknown) => api.post<PricedBasket>(`/shops/${shopId}/price-basket`, body));
  const update = (n: number, patch: Partial<PreviewLine>) => setLines((ls) => ls.map((l, i) => (i === n ? { ...l, ...patch } : l)));

  async function submit() {
    const valid = lines.filter((l) => l.productId !== "").map((l) => ({ productId: l.productId, quantity: Number(l.quantity), unitPriceCents: parseRandToCents(l.price) }));
    if (valid.length === 0 || valid.some((l) => !(l.quantity >= 1) || l.unitPriceCents === null)) { setProblem(t("previewNeedsLine")); return; }
    setProblem("");
    const r = await run.run({ deviceId, staffId, lines: valid, at: null });
    setResult(r.ok ? r.value : null);
  }

  return (
    <Card title={t("previewHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("previewHint")}</p>
          {lines.map((l, n) => (
            <Inline key={n}>
              <ProductSelect label={`${t("previewProduct")} ${n + 1}`} value={l.productId} products={products} onChange={(id) => update(n, { productId: id })} />
              <TextField label={`${t("previewQuantity")} ${n + 1}`} type="number" min={1} value={l.quantity} onChange={(e) => update(n, { quantity: e.target.value })} />
              <TextField label={`${t("previewPrice")} ${n + 1}`} inputMode="decimal" value={l.price} onChange={(e) => update(n, { price: e.target.value })} />
              <Button variant="quiet" onClick={() => setLines((ls) => ls.filter((_, i) => i !== n))}>{t("previewRemove", { n: n + 1 })}</Button>
            </Inline>
          ))}
          <Inline>
            <Button variant="secondary" onClick={() => setLines((ls) => [...ls, { productId: "", quantity: "1", price: "" }])}>{t("previewAdd")}</Button>
            <Button type="submit" disabled={run.busy || !ready}>{t("previewRun")}</Button>
          </Inline>
          {problem && <Notice kind="warning">{problem}</Notice>}
          {run.error && <Notice kind="danger">{run.error.message}</Notice>}
          {result && (
            <Stack>
              <dl>
                <dt>{t("subtotal")}</dt><dd>{formatRand(result.subtotalCents)}</dd>
                <dt>{t("discount")}</dt><dd>{formatRand(result.discountCents)}</dd>
                <dt>{t("total")}</dt><dd>{formatRand(result.totalCents)}</dd>
              </dl>
              {result.applied.length === 0 ? <p>{t("noneApplied")}</p> : (
                <ul aria-label={t("applied")}>{result.applied.map((a) => <li key={a.promotionId}>{t("appliedLine", { name: a.name, amount: formatRand(a.discountCents), times: a.times })}</li>)}</ul>
              )}
            </Stack>
          )}
        </Stack>
      </form>
    </Card>
  );
}
