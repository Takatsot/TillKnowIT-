import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, newId, parseRandToCents, queueSale } from "@tillknowit/domain";
import { useMemo, useRef, useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, suggestRef, TabGate, useAction, useKitT, useLoad, useT, who, type Caller, type Translate } from "../till-kit/kit";
import { LinesEditor, linesTotal } from "../till-kit/LinesEditor";
import { addDays, saDate, saDateTime } from "../till-kit/time";
import type { TicketLineDto } from "../till-kit/types";
import { laybyCollectSale } from "./collectSale";
import { messages, type LaybyKey } from "./messages";
import { cancellationFee, minimumDeposit, parseTerms, type LaybyTerms } from "./terms";

interface Layby {
  id: string;
  reference: string;
  customerName: string;
  customerPhone: string | null;
  lines: TicketLineDto[];
  totalCents: number;
  paidCents: number;
  balanceCents: number;
  dueOn: string;
  status: string;
  cancellationFeeCents: number;
  refundDueCents: number;
  payments: Array<{ amountCents: number; methodCode: string; paidAt: string }>;
}

const methods = ["cash", "card", "qr"] as const;
type Method = (typeof methods)[number];
const methodKey = (m: string) => (`method.${m}` as LaybyKey);

export function LaybyTab({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <LaybyBody ctx={ctx} caller={caller} />}</TabGate>;
}

function LaybyBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const kit = useKitT(ctx.language);
  const terms = useMemo(() => parseTerms(ctx.features.settings("layby")), [ctx.features]);
  const [screen, setScreen] = useState<{ kind: "list" } | { kind: "new" } | { kind: "detail"; id: string }>({ kind: "list" });
  const [info, setInfo] = useState("");
  const plans = useLoad(() => ctx.api.get<Layby[]>(`/shops/${caller.shopId}/laybys`, who(caller)), [caller.shopId]);

  if (screen.kind === "new") {
    return <NewLayby ctx={ctx} caller={caller} t={t} terms={terms} back={() => setScreen({ kind: "list" })} saved={(p) => { setInfo(t("new.saved", { ref: p.reference, balance: formatRand(p.balanceCents) })); plans.reload(); setScreen({ kind: "list" }); }} />;
  }
  const chosen = screen.kind === "detail" ? plans.data?.find((p) => p.id === screen.id) : undefined;
  if (chosen) {
    return <Detail key={chosen.id} ctx={ctx} caller={caller} t={t} terms={terms} plan={chosen} changed={plans.reload} back={() => setScreen({ kind: "list" })} />;
  }

  const statusText = (s: string) => (["active", "completed", "cancelled", "expired", "collected"].includes(s) ? t(`status.${s}` as LaybyKey) : s);
  return (
    <Card title={t("title")}>
      <Stack>
        <Inline><Button onClick={() => setScreen({ kind: "new" })}>{t("list.new")}</Button></Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={plans.error} t={kit} />
        {plans.loading && plans.data === null ? <p>{kit("kit.loading")}</p> : null}
        {plans.data ? (
          <Table
            empty={t("list.empty")}
            rows={plans.data}
            rowKey={(p) => p.id}
            columns={[
              { header: t("list.colRef"), cell: (p) => <Button variant="quiet" aria-label={t("list.open", { ref: p.reference })} onClick={() => setScreen({ kind: "detail", id: p.id })}>{p.reference}</Button> },
              { header: t("list.colCustomer"), cell: (p) => p.customerName },
              { header: t("list.colTotal"), cell: (p) => formatRand(p.totalCents), align: "right" },
              { header: t("list.colPaid"), cell: (p) => formatRand(p.paidCents), align: "right" },
              { header: t("list.colBalance"), cell: (p) => formatRand(p.balanceCents), align: "right" },
              { header: t("list.colDue"), cell: (p) => p.dueOn },
              { header: t("list.colStatus"), cell: (p) => <Badge>{statusText(p.status)}</Badge> },
            ]}
          />
        ) : null}
      </Stack>
    </Card>
  );
}

function NewLayby({ ctx, caller, t, terms, back, saved }: { ctx: TillContext; caller: Caller; t: Translate<LaybyKey>; terms: LaybyTerms; back: () => void; saved: (p: Layby) => void }) {
  const kit = useKitT(ctx.language);
  const [reference, setReference] = useState(() => suggestRef("L"));
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [lines, setLines] = useState<TicketLineDto[]>([]);
  const [deposit, setDeposit] = useState("");
  const [method, setMethod] = useState<Method>("cash");
  const [due, setDue] = useState(() => addDays(saDate(), Math.min(terms.maxWeeks, 4) * 7));
  const [problem, setProblem] = useState("");
  const [id] = useState(() => newId());
  const action = useAction();
  const total = linesTotal(lines);
  const min = minimumDeposit(total, terms);

  async function save() {
    if (reference.trim() === "") return setProblem(t("new.refRequired"));
    if (name.trim() === "") return setProblem(t("new.customerRequired"));
    if (lines.length === 0) return setProblem(t("new.noLines"));
    const cents = parseRandToCents(deposit);
    if (cents === null) return setProblem(t("new.depositInvalid"));
    if (cents < min) return setProblem(t("new.depositSmall", { min: formatRand(min) }));
    if (due === "") return setProblem(t("new.dueRequired"));
    setProblem("");
    const r = await action.run(() =>
      ctx.api.post<Layby>(`/shops/${caller.shopId}/laybys`, {
        ...who(caller), id, reference: reference.trim(), customerName: name.trim(), customerPhone: phone.trim() || null, customerId: null, lines, depositCents: cents, depositMethod: method, dueOn: due,
      }));
    if (r.ok) saved(r.value);
  }

  return (
    <Card title={t("new.title")}>
      <Stack>
        <Notice>{t("new.terms", { percent: terms.minDepositPercent, weeks: terms.maxWeeks })}</Notice>
        <TextField label={t("new.ref")} hint={t("new.refHint")} maxLength={20} value={reference} onChange={(e) => setReference(e.target.value)} />
        <TextField label={t("new.customer")} value={name} onChange={(e) => setName(e.target.value)} />
        <TextField label={t("new.phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} />
        <TextField label={t("new.deposit")} hint={total > 0 ? t("new.depositHint", { min: formatRand(min) }) : undefined} inputMode="decimal" value={deposit} onChange={(e) => setDeposit(e.target.value)} />
        <SelectField label={t("new.method")} value={method} onChange={(e) => setMethod(e.target.value as Method)}>
          {methods.map((m) => <option key={m} value={m}>{t(methodKey(m))}</option>)}
        </SelectField>
        <TextField label={t("new.due")} type="date" value={due} min={addDays(saDate(), 1)} max={addDays(saDate(), terms.maxWeeks * 7)} onChange={(e) => setDue(e.target.value)} />
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

function Detail({ ctx, caller, t, terms, plan: initial, changed, back }: { ctx: TillContext; caller: Caller; t: Translate<LaybyKey>; terms: LaybyTerms; plan: Layby; changed: () => void; back: () => void }) {
  const kit = useKitT(ctx.language);
  const [plan, setPlan] = useState(initial);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<Method>("cash");
  const [problem, setProblem] = useState("");
  const [info, setInfo] = useState("");
  const [sure, setSure] = useState(false);
  const saleId = useRef<string | null>(null);
  const action = useAction();
  const base = `/shops/${caller.shopId}/laybys/${plan.id}`;

  const update = (p: Layby, text: string) => {
    setPlan(p);
    setInfo(text);
    changed();
  };

  async function pay() {
    const cents = parseRandToCents(amount);
    if (cents === null || cents <= 0) return setProblem(t("detail.payInvalid"));
    if (cents > plan.balanceCents) return setProblem(t("detail.payTooMuch", { amount: formatRand(plan.balanceCents) }));
    setProblem("");
    const r = await action.run(() => ctx.api.post<Layby>(`${base}/payments`, { ...who(caller), amountCents: cents, methodCode: method }));
    if (r.ok) {
      setAmount("");
      update(r.value, t("detail.paid", { amount: formatRand(cents) }));
    }
  }

  /** Rings the goods up as a sale paid by lay-by, queues it, and tells the server. The sale id is kept so pressing again is never a second sale. */
  async function collect() {
    const now = new Date();
    saleId.current ??= newId(now.getTime());
    const sale = laybyCollectSale(plan.lines, ctx.catalogue, { saleId: saleId.current, staffId: caller.staffId }, now);
    await queueSale(ctx.outbox, sale, now);
    ctx.nudgeSync();
    const r = await action.run(() => ctx.api.post<Layby>(`${base}/collect`, { ...who(caller), saleId: saleId.current }));
    if (r.ok) update(r.value, t("detail.collected"));
    else if (r.code === "layby.sale_unknown") {
      action.clear();
      setInfo(t("detail.collectWait"));
    }
  }

  async function cancel() {
    const r = await action.run(() => ctx.api.post<Layby>(`${base}/cancel`, who(caller)));
    setSure(false);
    if (r.ok) update(r.value, t("detail.cancelled", { fee: formatRand(r.value.cancellationFeeCents), refund: formatRand(r.value.refundDueCents) }));
  }

  const fee = cancellationFee(plan.totalCents, plan.paidCents, terms);
  return (
    <Card title={t("detail.title", { ref: plan.reference })}>
      <Stack>
        <Inline>
          <Badge>{t(`status.${plan.status}` as LaybyKey)}</Badge>
          <span>{t("detail.for", { name: plan.customerName })}</span>
          <Button variant="quiet" onClick={back}>{t("detail.back")}</Button>
        </Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={action.error} t={kit} />
        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={plan.lines} onChange={() => undefined} readOnly />
        <p>{t("detail.balance", { amount: formatRand(plan.balanceCents), total: formatRand(plan.totalCents) })}</p>
        {plan.status === "cancelled" || plan.status === "expired" ? <p>{t("detail.finalFee", { fee: formatRand(plan.cancellationFeeCents), refund: formatRand(plan.refundDueCents) })}</p> : null}

        <h3>{t("detail.payments")}</h3>
        <Table
          empty={t("detail.noPayments")}
          rows={plan.payments}
          rowKey={(p) => `${p.paidAt}-${p.amountCents}`}
          columns={[
            { header: t("detail.payColAt"), cell: (p) => saDateTime(p.paidAt) },
            { header: t("detail.payColMethod"), cell: (p) => t(methodKey(p.methodCode)) },
            { header: t("detail.payColAmount"), cell: (p) => formatRand(p.amountCents), align: "right" },
          ]}
        />

        {plan.status === "active" ? (
          <>
            <Inline>
              <TextField label={t("detail.payAmount")} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} error={problem || null} />
              <SelectField label={t("detail.payMethod")} value={method} onChange={(e) => setMethod(e.target.value as Method)}>
                {methods.map((m) => <option key={m} value={m}>{t(methodKey(m))}</option>)}
              </SelectField>
              <Button onClick={() => void pay()} disabled={action.busy}>{t("detail.pay")}</Button>
            </Inline>
            {sure ? (
              <Notice kind="warning">
                <Stack>
                  {t("detail.cancelFee", { fee: formatRand(fee), refund: formatRand(plan.paidCents - fee) })}
                  <Inline>
                    <Button variant="secondary" onClick={() => void cancel()} disabled={action.busy}>{t("detail.cancelYes")}</Button>
                    <Button variant="quiet" onClick={() => setSure(false)}>{t("detail.cancelNo")}</Button>
                  </Inline>
                </Stack>
              </Notice>
            ) : <Button variant="quiet" onClick={() => setSure(true)}>{t("detail.cancel")}</Button>}
          </>
        ) : null}
        {plan.status === "completed" ? <Inline><Button onClick={() => void collect()} disabled={action.busy}>{t("detail.collect")}</Button></Inline> : null}
      </Stack>
    </Card>
  );
}
