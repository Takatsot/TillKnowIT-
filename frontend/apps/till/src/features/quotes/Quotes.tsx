import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, newId } from "@tillknowit/domain";
import { useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, saveFile, suggestRef, TabGate, useAction, useKitT, useLoad, useT, who, type Caller, type Translate } from "../till-kit/kit";
import { LinesEditor } from "../till-kit/LinesEditor";
import { addLinesToSale } from "../till-kit/saleBridge";
import { PayPanel } from "../till-kit/PayPanel";
import { addDays, saDate } from "../till-kit/time";
import type { TicketLineDto, TicketResponse } from "../till-kit/types";
import { messages, type QuotesKey } from "./messages";

interface Quote {
  id: string;
  number: string;
  customerName: string | null;
  customerPhone: string | null;
  lines: TicketLineDto[];
  totalCents: number;
  expiresOn: string;
  status: string;
  ticketId: string | null;
}

type Screen = { kind: "list" } | { kind: "new" } | { kind: "converted"; quote: Quote; ticket: TicketResponse } | { kind: "pay"; quote: Quote; ticket: TicketResponse };

export function Quotes({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <QuotesBody ctx={ctx} caller={caller} />}</TabGate>;
}

function QuotesBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const kit = useKitT(ctx.language);
  const [screen, setScreen] = useState<Screen>({ kind: "list" });
  const [info, setInfo] = useState("");
  const [sure, setSure] = useState<string | null>(null);
  const quotes = useLoad(() => ctx.api.get<Quote[]>(`/shops/${caller.shopId}/quotes`, who(caller)), [caller.shopId]);
  const action = useAction();

  const convert = async (q: Quote) => {
    const r = await action.run(() => ctx.api.post<TicketResponse>(`/shops/${caller.shopId}/quotes/${q.id}/convert`, who(caller)));
    if (r.ok) {
      quotes.reload();
      setInfo("");
      setScreen({ kind: "converted", quote: q, ticket: r.value });
    }
  };
  /** Puts the quote's goods into the sale being rung up on the Sell tab, at the quoted prices, then shows that tab. */
  const addToSale = async (q: Quote) => {
    const added = addLinesToSale(ctx, q.lines);
    if (!added.ok) return setInfo(t("addToSale.failed", { name: added.name, message: added.message }));
    // The goods are on the sale now; marking the quote converted needs the server, and its failure must not undo the sale.
    const marked = await ctx.api.post<void>(`/shops/${caller.shopId}/quotes/${q.id}/to-sale`, who(caller));
    setInfo(t(marked.ok ? "addToSale.done" : "addToSale.notMarked", { number: q.number }));
    if (marked.ok) quotes.reload();
    ctx.openSell?.();
  };
  const cancel = async (q: Quote) => {
    const r = await action.run(() => ctx.api.post<void>(`/shops/${caller.shopId}/quotes/${q.id}/cancel`, who(caller)));
    setSure(null);
    if (r.ok) {
      setInfo(t("cancel.done", { number: q.number }));
      quotes.reload();
    }
  };
  const pdf = async (q: Quote) => {
    const r = await action.run(() => ctx.api.download(`/shops/${caller.shopId}/quotes/${q.id}/pdf`, who(caller)));
    if (r.ok) saveFile(r.value);
  };

  if (screen.kind === "new") {
    return <NewQuote ctx={ctx} caller={caller} t={t} back={() => setScreen({ kind: "list" })} saved={(q) => { setInfo(t("new.saved", { number: q.number })); quotes.reload(); setScreen({ kind: "list" }); }} />;
  }
  if (screen.kind === "converted") {
    const { quote, ticket } = screen;
    return (
      <Card title={t("title")}>
        <Stack>
          <Notice kind="success">{t("convert.done", { number: quote.number, label: ticket.label })}</Notice>
          <Inline>
            <Button onClick={() => setScreen({ kind: "pay", quote, ticket })}>{t("convert.pay")}</Button>
            <Button variant="secondary" onClick={() => setScreen({ kind: "list" })}>{t("convert.later")}</Button>
          </Inline>
        </Stack>
      </Card>
    );
  }
  if (screen.kind === "pay") {
    const { quote, ticket } = screen;
    return <PayPanel ctx={ctx} caller={caller} ticket={ticket} onBack={() => setScreen({ kind: "converted", quote, ticket })} onPaid={(text) => { setInfo(`${t("convert.paid", { number: quote.number })} ${text}`); setScreen({ kind: "list" }); }} />;
  }

  const statusText = (s: string) => (["open", "converted", "expired", "cancelled"].includes(s) ? t(`status.${s}` as QuotesKey) : s);

  return (
    <Card title={t("title")}>
      <Stack>
        <Inline><Button onClick={() => setScreen({ kind: "new" })}>{t("list.new")}</Button></Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={quotes.error ?? action.error} t={kit} />
        {quotes.loading && quotes.data === null ? <p>{kit("kit.loading")}</p> : null}
        {sure ? (
          <Notice kind="warning">
            <Stack>
              {t("cancel.sure", { number: quotes.data?.find((q) => q.id === sure)?.number ?? "" })}
              <Inline>
                <Button variant="secondary" disabled={action.busy} onClick={() => { const q = quotes.data?.find((x) => x.id === sure); if (q) void cancel(q); }}>{t("cancel.yes")}</Button>
                <Button variant="quiet" onClick={() => setSure(null)}>{t("cancel.no")}</Button>
              </Inline>
            </Stack>
          </Notice>
        ) : null}
        {quotes.data || quotes.error ? (
          <Table
            empty={t("list.empty")}
            rows={quotes.data ?? []}
            rowKey={(q) => q.id}
            columns={[
              { header: t("list.colNumber"), cell: (q) => q.number },
              { header: t("list.colCustomer"), cell: (q) => q.customerName ?? t("list.noCustomer") },
              { header: t("list.colTotal"), cell: (q) => formatRand(q.totalCents), align: "right" },
              { header: t("list.colExpires"), cell: (q) => q.expiresOn },
              { header: t("list.colStatus"), cell: (q) => <Badge>{statusText(q.status)}</Badge> },
              {
                header: t("list.colActions"),
                cell: (q) => (
                  <Inline>
                    {q.status === "open" ? (
                      <>
                        <Button aria-label={t("act.convert", { number: q.number })} disabled={action.busy} onClick={() => void convert(q)}>{t("act.convert", { number: q.number })}</Button>
                        {ctx.sale ? <Button variant="secondary" aria-label={t("act.addToSale", { number: q.number })} onClick={() => void addToSale(q)}>{t("act.addToSale", { number: q.number })}</Button> : null}
                        <Button variant="quiet" aria-label={t("act.cancel", { number: q.number })} onClick={() => setSure(q.id)}>{t("act.cancel", { number: q.number })}</Button>
                      </>
                    ) : null}
                    <Button variant="secondary" aria-label={t("act.pdf", { number: q.number })} disabled={action.busy} onClick={() => void pdf(q)}>PDF</Button>
                  </Inline>
                ),
              },
            ]}
          />
        ) : null}
      </Stack>
    </Card>
  );
}

function NewQuote({ ctx, caller, t, back, saved }: { ctx: TillContext; caller: Caller; t: Translate<QuotesKey>; back: () => void; saved: (q: Quote) => void }) {
  const kit = useKitT(ctx.language);
  const [number, setNumber] = useState(() => suggestRef("Q"));
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [expires, setExpires] = useState(() => addDays(saDate(), 14));
  const [lines, setLines] = useState<TicketLineDto[]>([]);
  const [problem, setProblem] = useState("");
  const [id] = useState(() => newId());
  const action = useAction();

  async function save() {
    if (number.trim() === "") return setProblem(t("new.numberRequired"));
    if (lines.length === 0) return setProblem(t("new.noLines"));
    if (expires === "") return setProblem(t("new.expiresRequired"));
    setProblem("");
    const r = await action.run(() =>
      ctx.api.post<Quote>(`/shops/${caller.shopId}/quotes`, {
        ...who(caller), id, number: number.trim(), customerName: name.trim() || null, customerPhone: phone.trim() || null, customerId: null, lines, expiresOn: expires,
      }));
    if (r.ok) saved(r.value);
  }

  return (
    <Card title={t("new.title")}>
      <Stack>
        <TextField label={t("new.number")} hint={t("new.numberHint")} maxLength={20} value={number} onChange={(e) => setNumber(e.target.value)} />
        <TextField label={t("new.customer")} value={name} onChange={(e) => setName(e.target.value)} />
        <TextField label={t("new.phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
        <TextField label={t("new.expires")} hint={t("new.expiresHint")} type="date" value={expires} min={saDate()} max={addDays(saDate(), 90)} onChange={(e) => setExpires(e.target.value)} />
        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} />
        {problem ? <Notice kind="danger">{problem}</Notice> : null}
        <ErrorNotice error={action.error} t={kit} />
        <Inline>
          <Button onClick={() => void save()} disabled={action.busy}>{t("new.save")}</Button>
          <Button variant="quiet" onClick={back}>{t("new.back")}</Button>
        </Inline>
      </Stack>
    </Card>
  );
}
