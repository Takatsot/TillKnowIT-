import { Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, newId } from "@tillknowit/domain";
import { useRef, useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, TabGate, useAction, useKitT, useLoad, useT, type Caller, type Translate } from "../till-kit/kit";
import { saDate, saDateTime } from "../till-kit/time";
import { addCustomer, findCustomers, getPoints, newPointsReference, redeemPoints, type Customer, type PointsRedemption } from "./customerApi";
import { messages, type CustomersKey } from "./messages";

export function Customers({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <CustomersBody ctx={ctx} caller={caller} />}</TabGate>;
}

function CustomersBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const kit = useKitT(ctx.language);
  const [search, setSearch] = useState("");
  const [problem, setProblem] = useState("");
  const [results, setResults] = useState<Customer[] | null>(null);
  const [screen, setScreen] = useState<{ kind: "find" } | { kind: "add" } | { kind: "customer"; customer: Customer }>({ kind: "find" });
  const [info, setInfo] = useState("");
  const action = useAction();

  async function find() {
    if (search.trim().length < 3) return setProblem(t("find.short"));
    setProblem("");
    setInfo("");
    const r = await action.run(() => findCustomers(ctx.api, caller, search));
    if (r.ok) setResults(r.value);
  }

  if (screen.kind === "add") {
    return <AddCustomer ctx={ctx} caller={caller} t={t} initialPhone={/^[\d\s+]+$/.test(search) ? search.trim() : ""} back={() => setScreen({ kind: "find" })} saved={(c) => { setInfo(t("add.saved", { name: c.name })); setScreen({ kind: "customer", customer: c }); }} />;
  }
  if (screen.kind === "customer") {
    return <CustomerView key={screen.customer.id} ctx={ctx} caller={caller} t={t} customer={screen.customer} info={info} back={() => setScreen({ kind: "find" })} />;
  }

  return (
    <Card title={t("title")}>
      <Stack>
        <TextField label={t("find.label")} hint={t("find.hint")} value={search} onChange={(e) => setSearch(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") void find(); }} error={problem || null} autoComplete="off" />
        <ErrorNotice error={action.error} t={kit} />
        <Inline>
          <Button onClick={() => void find()} disabled={action.busy}>{t("find.button")}</Button>
          <Button variant="secondary" onClick={() => setScreen({ kind: "add" })}>{t("add.show")}</Button>
        </Inline>
        {results ? (
          <Table
            empty={t("find.none")}
            rows={results}
            rowKey={(c) => c.id}
            columns={[
              { header: t("find.colName"), cell: (c) => <Button variant="quiet" aria-label={t("find.open", { name: c.name })} onClick={() => setScreen({ kind: "customer", customer: c })}>{c.name}</Button> },
              { header: t("find.colPhone"), cell: (c) => c.phone },
              ...(ctx.features.isOn("loyalty") ? [{ header: t("find.colPoints"), cell: (c: Customer) => String(c.points), align: "right" as const }] : []),
            ]}
          />
        ) : null}
      </Stack>
    </Card>
  );
}

function AddCustomer({ ctx, caller, t, initialPhone, back, saved }: { ctx: TillContext; caller: Caller; t: Translate<CustomersKey>; initialPhone: string; back: () => void; saved: (c: Customer) => void }) {
  const kit = useKitT(ctx.language);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState(initialPhone);
  const [email, setEmail] = useState("");
  const [consent, setConsent] = useState(false);
  const [problem, setProblem] = useState("");
  const [id] = useState(() => newId());
  const action = useAction();

  async function save() {
    if (name.trim() === "") return setProblem(t("add.nameRequired"));
    if (phone.trim() === "") return setProblem(t("add.phoneRequired"));
    setProblem("");
    const r = await action.run(() => addCustomer(ctx.api, caller, { id, name: name.trim(), phone: phone.trim(), email: email.trim() || null, messageConsent: consent }));
    if (r.ok) saved(r.value);
  }

  return (
    <Card title={t("add.title")}>
      <Stack>
        <TextField label={t("add.name")} value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        <TextField label={t("add.phone")} hint={t("add.phoneHint")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
        <TextField label={t("add.email")} type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        <CheckField label={t("add.consent")} checked={consent} onChange={(e) => setConsent(e.target.checked)} />
        <small>{t("add.consentHint")}</small>
        {problem ? <Notice kind="danger">{problem}</Notice> : null}
        <ErrorNotice error={action.error} t={kit} />
        <Inline>
          <Button onClick={() => void save()} disabled={action.busy}>{t("add.save")}</Button>
          <Button variant="quiet" onClick={back}>{t("add.back")}</Button>
        </Inline>
      </Stack>
    </Card>
  );
}

function CustomerView({ ctx, caller, t, customer, info, back }: { ctx: TillContext; caller: Caller; t: Translate<CustomersKey>; customer: Customer; info: string; back: () => void }) {
  const loyalty = ctx.features.isOn("loyalty");
  return (
    <Card title={t("detail.title", { name: customer.name })}>
      <Stack>
        <Inline>
          <Button variant="quiet" onClick={back}>{t("detail.back")}</Button>
          {ctx.sale ? <Button variant="secondary" onClick={() => { ctx.sale?.setCustomer(customer.id, customer.name); ctx.openSell?.(); }}>{t("detail.attach", { name: customer.name })}</Button> : null}
        </Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <p>{t("detail.phone", { phone: customer.phone })}</p>
        <p>{customer.messageConsent ? (customer.consentAt ? t("detail.consentYes", { date: saDateTime(customer.consentAt).slice(0, 10) || saDate() }) : t("detail.consentYesNoDate")) : t("detail.consentNo")}</p>
        {loyalty ? <PointsPanel ctx={ctx} caller={caller} t={t} customer={customer} /> : null}
      </Stack>
    </Card>
  );
}

function PointsPanel({ ctx, caller, t, customer }: { ctx: TillContext; caller: Caller; t: Translate<CustomersKey>; customer: Customer }) {
  const kit = useKitT(ctx.language);
  const points = useLoad(() => getPoints(ctx.api, caller, customer.id), [customer.id]);
  const [amount, setAmount] = useState("");
  const [problem, setProblem] = useState("");
  const [done, setDone] = useState<(PointsRedemption & { reference: string }) | null>(null);
  const reference = useRef(newPointsReference());
  const action = useAction();
  const data = points.data;

  async function redeem() {
    const n = Number(amount);
    if (!Number.isInteger(n) || n <= 0) return setProblem(t("points.invalid"));
    if (data && n > data.points) return setProblem(t("points.tooMany", { points: data.points }));
    setProblem("");
    const used = reference.current;
    const r = await action.run(() => redeemPoints(ctx.api, caller, customer.id, n, used));
    if (r.ok) {
      setDone({ ...r.value, reference: r.value.redemptionReference });
      setAmount("");
      reference.current = newPointsReference();
      points.reload();
    }
  }

  const typeText = (type: string) => (["earn", "redeem", "adjust", "reverse"].includes(type) ? t(`type.${type}` as CustomersKey) : type);
  return (
    <Stack>
      <h3>{t("points.title")}</h3>
      <ErrorNotice error={points.error} t={kit} />
      {data ? (
        <>
          <p>{t("points.balance", { points: data.points, value: formatRand(data.valueCents) })}</p>
          <p>{t("points.min", { min: data.minRedeemPoints })}</p>
          <Table
            empty={t("points.none")}
            rows={data.recent}
            rowKey={(e) => `${e.at}-${e.type}-${e.points}`}
            columns={[
              { header: t("points.colWhen"), cell: (e) => saDateTime(e.at) },
              { header: t("points.colType"), cell: (e) => typeText(e.type) },
              { header: t("points.colPoints"), cell: (e) => String(e.points), align: "right" },
            ]}
          />
          <TextField label={t("points.redeem")} inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} error={problem || null} />
          <ErrorNotice error={action.error} t={kit} />
          <Inline><Button onClick={() => void redeem()} disabled={action.busy}>{t("points.redeemButton")}</Button></Inline>
          {done ? (
            <Notice kind="success">
              <Stack>
                <span>{t("points.done", { points: done.points, value: formatRand(done.valueCents), left: done.balanceAfter })}</span>
                <span>{t("points.reference", { reference: done.reference })}</span>
                <span>{t("points.referenceNote")}</span>
              </Stack>
            </Notice>
          ) : null}
        </>
      ) : null}
    </Stack>
  );
}
