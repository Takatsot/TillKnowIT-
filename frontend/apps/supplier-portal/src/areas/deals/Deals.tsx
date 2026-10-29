import { formatDateTime, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import { useProducts } from "./useProducts";

export interface Deal { id: string; title: string; type: string; startsAt: string; endsAt: string; areas: string[]; isSponsored: boolean; label: string | null; isActive: boolean }

type Item = { productId: string; cases: string };

function NewDeal({ supplierId, onPosted }: { supplierId: string; onPosted: (title: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const products = useProducts(supplierId);
  const [title, setTitle] = useState("");
  const [type, setType] = useState("discount");
  const [product, setProduct] = useState("");
  const [percent, setPercent] = useState("");
  const [centsOff, setCentsOff] = useState("");
  const [buy, setBuy] = useState("");
  const [free, setFree] = useState("");
  const [items, setItems] = useState<Item[]>([{ productId: "", cases: "1" }, { productId: "", cases: "1" }]);
  const [comboPrice, setComboPrice] = useState("");
  const [starts, setStarts] = useState("");
  const [ends, setEnds] = useState("");
  const [areas, setAreas] = useState("");
  const [sponsored, setSponsored] = useState(false);
  const [problem, setProblem] = useState("");
  const post = useAction((body: object) => api.post<Deal>(`/suppliers/${supplierId}/deals`, body));
  const list = products.data ?? [];
  const num = (v: string) => (v.trim() === "" ? null : Number(v));

  async function submit() {
    const codes = areas.split(",").map((a) => a.trim()).filter(Boolean);
    if (codes.length === 0) { setProblem(t("needAreas")); return; }
    if (starts === "" || ends === "") { setProblem(t("needDates")); return; }
    const terms: Record<string, unknown> = { supplierProductId: null, percentOff: null, centsOffPerCase: null, buyCases: null, freeCases: null, items: null, comboPriceCents: null };
    if (type === "discount") {
      const off = centsOff.trim() === "" ? null : parseRandToCents(centsOff);
      if ((centsOff.trim() !== "" && off === null) || [num(percent)].some((n) => n !== null && Number.isNaN(n))) { setProblem(t("badNumber")); return; }
      Object.assign(terms, { supplierProductId: product || null, percentOff: num(percent), centsOffPerCase: off });
    } else if (type === "free_goods") {
      const b = num(buy), f = num(free);
      if (Number.isNaN(b) || Number.isNaN(f)) { setProblem(t("badNumber")); return; }
      Object.assign(terms, { supplierProductId: product || null, buyCases: b, freeCases: f });
    } else {
      const price = parseRandToCents(comboPrice);
      if (price === null) { setProblem(t("badMoney")); return; }
      Object.assign(terms, { items: items.map((i) => ({ supplierProductId: i.productId, cases: Number(i.cases) })), comboPriceCents: price });
    }
    setProblem("");
    const r = await post.run({
      callerPersonId: personId, title: title.trim(), type, terms, startsAt: new Date(starts).toISOString(), endsAt: new Date(ends).toISOString(), areas: codes, isSponsored: sponsored,
    });
    if (r.ok) { onPosted(r.value.title); setTitle(""); }
  }

  const productSelect = (label: string, value: string, onChange: (v: string) => void) => (
    <SelectField label={label} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{t("chooseProduct")}</option>
      {list.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
    </SelectField>
  );

  return (
    <Card title={t("newDeal")}>
      {products.error && <Notice kind="danger">{products.error.message}</Notice>}
      {!products.loading && !products.error && list.length === 0 && <Notice kind="info">{t("needProducts")}</Notice>}
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("fTitle")} value={title} onChange={(e) => setTitle(e.target.value)} required />
            <SelectField label={t("fType")} value={type} onChange={(e) => setType(e.target.value)}>
              <option value="discount">{t("discount")}</option>
              <option value="free_goods">{t("freeGoods")}</option>
              <option value="combo">{t("combo")}</option>
            </SelectField>
          </Inline>
          {type === "discount" && (
            <Inline>
              {productSelect(t("fProduct"), product, setProduct)}
              <TextField label={t("fPercent")} hint={t("discountHint")} inputMode="numeric" value={percent} onChange={(e) => setPercent(e.target.value)} />
              <TextField label={t("fCentsOff")} inputMode="decimal" value={centsOff} onChange={(e) => setCentsOff(e.target.value)} />
            </Inline>
          )}
          {type === "free_goods" && (
            <Inline>
              {productSelect(t("fProduct"), product, setProduct)}
              <TextField label={t("fBuy")} inputMode="numeric" value={buy} onChange={(e) => setBuy(e.target.value)} />
              <TextField label={t("fFree")} inputMode="numeric" value={free} onChange={(e) => setFree(e.target.value)} />
            </Inline>
          )}
          {type === "combo" && (
            <Stack>
              {items.map((it, i) => (
                <Inline key={i}>
                  {productSelect(t("comboProduct", { n: i + 1 }), it.productId, (v) => setItems(items.map((x, n) => (n === i ? { ...x, productId: v } : x))))}
                  <TextField label={t("comboCases", { n: i + 1 })} inputMode="numeric" value={it.cases} onChange={(e) => setItems(items.map((x, n) => (n === i ? { ...x, cases: e.target.value } : x)))} />
                </Inline>
              ))}
              <Inline>
                <Button variant="secondary" onClick={() => setItems([...items, { productId: "", cases: "1" }])}>{t("addItem")}</Button>
                <TextField label={t("fComboPrice")} inputMode="decimal" value={comboPrice} onChange={(e) => setComboPrice(e.target.value)} />
              </Inline>
            </Stack>
          )}
          <Inline>
            <TextField label={t("fStarts")} type="datetime-local" value={starts} onChange={(e) => setStarts(e.target.value)} />
            <TextField label={t("fEnds")} type="datetime-local" value={ends} onChange={(e) => setEnds(e.target.value)} />
            <TextField label={t("fAreas")} hint={t("fAreasHint")} value={areas} onChange={(e) => setAreas(e.target.value)} />
          </Inline>
          <CheckField label={t("fSponsored")} checked={sponsored} onChange={(e) => setSponsored(e.target.checked)} />
          {sponsored && <Notice kind="info">{t("sponsoredHint")}</Notice>}
          <div><Button type="submit" disabled={post.busy || title.trim() === ""}>{t("post")}</Button></div>
          {problem && <Notice kind="danger">{problem}</Notice>}
          {post.error && <Notice kind="danger">{post.error.message}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

export function Deals({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Deal[]>(`/suppliers/${supplierId}/deals`, { callerPersonId: personId }), [supplierId, personId]);
  const [note, setNote] = useState("");
  const cancel = useAction((deal: Deal) => api.patch<void>(`/deals/${deal.id}/cancel`, { callerPersonId: personId }));

  return (
    <Stack>
      {note && <Notice kind="success">{note}</Notice>}
      {cancel.error && <Notice kind="danger">{cancel.error.message}</Notice>}
      <Card title={t("dealsHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(d) => d.id}
            empty={t("dealsEmpty")}
            columns={[
              { header: t("colTitle"), cell: (d) => <>{d.title} {d.isSponsored && <Badge>{d.label ?? t("sponsoredLabel")}</Badge>}</> },
              { header: t("colType"), cell: (d) => d.type },
              { header: t("colRuns"), cell: (d) => `${formatDateTime(d.startsAt)} - ${formatDateTime(d.endsAt)}` },
              { header: t("colAreas"), cell: (d) => d.areas.join(", ") },
              { header: t("colState"), cell: (d) => <Badge>{d.isActive ? t("active") : t("notActive")}</Badge> },
              { header: "", cell: (d) => <Button variant="secondary" disabled={cancel.busy} aria-label={t("cancel", { title: d.title })} onClick={() => void cancel.run(d).then((r) => { if (r.ok) { setNote(t("cancelled", { title: d.title })); list.reload(); } })}>{t("cancelShort")}</Button> },
            ]}
          />
        )}
      </Card>
      <NewDeal supplierId={supplierId} onPosted={(title) => { setNote(t("posted", { title })); list.reload(); }} />
    </Stack>
  );
}
