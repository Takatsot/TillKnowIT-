import { newId } from "@tillknowit/domain";
import { formatDate, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { ProductSelect, useProducts } from "../selling-common/products";
import type { TillCaller } from "../selling-common/TillCaller";
import { messages } from "./messages";

interface Quote { id: string; number: string; customerName: string | null; customerPhone: string | null; lines: { productId: string; name: string; quantity: number; priceCents: number }[]; totalCents: number; expiresOn: string; status: string; ticketId: string | null }
interface Line { productId: string; quantity: string; price: string }

export function Quotes({ caller }: { caller: TillCaller }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const products = useProducts();
  const till = { deviceId: caller.deviceId, staffId: caller.staffId };
  const list = useLoad(
    () => (caller.ready ? api.get<Quote[]>(`/shops/${shopId}/quotes`, till) : Promise.resolve({ ok: true as const, value: [] as Quote[] })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const [note, setNote] = useState("");
  const act = useAction((q: Quote, what: "convert" | "cancel") => api.post<unknown>(`/shops/${shopId}/quotes/${q.id}/${what}`, till));
  const pdf = useAction((q: Quote) => api.download(`/shops/${shopId}/quotes/${q.id}/pdf`, till));

  async function run(q: Quote, what: "convert" | "cancel") {
    setNote("");
    const r = await act.run(q, what);
    if (r.ok) { setNote(t(what === "convert" ? "quoteConverted" : "quoteCancelled", { number: q.number })); list.reload(); }
  }

  async function openPdf(q: Quote) {
    const r = await pdf.run(q);
    if (r.ok) window.open(URL.createObjectURL(r.value.blob), "_blank", "noopener");
  }

  return (
    <Stack>
      <Card title={t("quotesHeading")}>
        <Stack>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {act.error && <Notice kind="danger">{act.error.message}</Notice>}
          {pdf.error && <Notice kind="danger">{pdf.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(q) => q.id}
              empty={t("quotesEmpty")}
              columns={[
                { header: t("colNumber"), cell: (q) => q.number },
                { header: t("colCustomer"), cell: (q) => q.customerName ?? "" },
                { header: t("colTotal"), cell: (q) => formatRand(q.totalCents), align: "right" },
                { header: t("colExpires"), cell: (q) => formatDate(q.expiresOn) },
                { header: t("colStatus"), cell: (q) => <Badge>{q.status}</Badge> },
                {
                  header: t("colActions"),
                  cell: (q) => (
                    <Inline>
                      <Button variant="secondary" disabled={pdf.busy} onClick={() => void openPdf(q)}>{t("pdf", { number: q.number })}</Button>
                      {q.status === "open" && <Button disabled={act.busy} onClick={() => void run(q, "convert")}>{t("convert", { number: q.number })}</Button>}
                      {q.status === "open" && <Button variant="quiet" disabled={act.busy} onClick={() => void run(q, "cancel")}>{t("cancelQuote", { number: q.number })}</Button>}
                    </Inline>
                  ),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      <NewQuote caller={caller} products={products.data ?? []} onSaved={(q) => { setNote(t("quoteSaved", { number: q.number })); list.reload(); }} />
    </Stack>
  );
}

function NewQuote({ caller, products, onSaved }: { caller: TillCaller; products: { productId: string; name: string }[]; onSaved: (q: Quote) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [number, setNumber] = useState("");
  const [customer, setCustomer] = useState("");
  const [phone, setPhone] = useState("");
  const [expires, setExpires] = useState("");
  const [lines, setLines] = useState<Line[]>([{ productId: "", quantity: "1", price: "" }]);
  const [problem, setProblem] = useState("");
  const save = useAction((body: unknown) => api.post<Quote>(`/shops/${shopId}/quotes`, body));
  const update = (n: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, i) => (i === n ? { ...l, ...patch } : l)));

  async function submit() {
    const chosen = lines.filter((l) => l.productId !== "").map((l) => ({
      productId: l.productId, name: products.find((p) => p.productId === l.productId)?.name ?? "", quantity: Number(l.quantity), priceCents: parseRandToCents(l.price), unitDivisor: 1,
    }));
    if (chosen.length === 0 || chosen.some((l) => !(l.quantity >= 1) || l.priceCents === null)) { setProblem(t("quoteNeedsLine")); return; }
    setProblem("");
    const r = await save.run({
      deviceId: caller.deviceId, staffId: caller.staffId, id: newId(), number: number.trim(), customerName: customer.trim() === "" ? null : customer.trim(),
      customerPhone: phone.trim() === "" ? null : phone.trim(), customerId: null, lines: chosen, expiresOn: expires,
    });
    if (r.ok) { onSaved(r.value); setNumber(""); setCustomer(""); setPhone(""); setLines([{ productId: "", quantity: "1", price: "" }]); }
  }

  return (
    <Card title={t("quoteNewHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("quoteNumber")} value={number} onChange={(e) => setNumber(e.target.value)} required />
            <TextField label={t("quoteCustomer")} value={customer} onChange={(e) => setCustomer(e.target.value)} />
            <TextField label={t("quotePhone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
            <TextField label={t("quoteExpires")} type="date" value={expires} onChange={(e) => setExpires(e.target.value)} required />
          </Inline>
          {lines.map((l, n) => (
            <Inline key={n}>
              <ProductSelect label={`${t("quoteProduct")} ${n + 1}`} value={l.productId} products={products} onChange={(id) => update(n, { productId: id })} />
              <TextField label={`${t("quoteQuantity")} ${n + 1}`} type="number" min={1} value={l.quantity} onChange={(e) => update(n, { quantity: e.target.value })} />
              <TextField label={`${t("quotePrice")} ${n + 1}`} inputMode="decimal" value={l.price} onChange={(e) => update(n, { price: e.target.value })} />
              <Button variant="quiet" onClick={() => setLines((ls) => ls.filter((_, i) => i !== n))}>{t("quoteRemoveLine", { n: n + 1 })}</Button>
            </Inline>
          ))}
          {problem && <Notice kind="warning">{problem}</Notice>}
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          <Inline>
            <Button variant="secondary" onClick={() => setLines((ls) => [...ls, { productId: "", quantity: "1", price: "" }])}>{t("quoteAddLine")}</Button>
            <Button type="submit" disabled={save.busy || !caller.ready || number.trim() === "" || expires === ""}>{t("quoteSave")}</Button>
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}
