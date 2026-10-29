import { Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, isSaleEntry, isUuid, ok, type SaleUploadDto } from "@tillknowit/domain";
import { useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, TabGate, useAction, useKitT, useLoad, useT, type Caller, type Translate } from "../till-kit/kit";
import { saDateTime } from "../till-kit/time";
import { messages, type SerialsKey } from "./messages";
import { findSerials, recordSerials, type SerialItem, type SerialRow } from "./serialApi";

export function Serials({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <SerialsBody ctx={ctx} caller={caller} />}</TabGate>;
}

function SerialsBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const [mode, setMode] = useState<"record" | "search">("record");
  return (
    <Card title={t("title")}>
      <Stack>
        <Inline>
          <Button variant={mode === "record" ? "primary" : "secondary"} aria-pressed={mode === "record"} onClick={() => setMode("record")}>{t("tab.record")}</Button>
          <Button variant={mode === "search" ? "primary" : "secondary"} aria-pressed={mode === "search"} onClick={() => setMode("search")}>{t("tab.search")}</Button>
        </Inline>
        {mode === "record" ? <Record ctx={ctx} caller={caller} t={t} /> : <Search ctx={ctx} caller={caller} t={t} />}
      </Stack>
    </Card>
  );
}

function Record({ ctx, caller, t }: { ctx: TillContext; caller: Caller; t: Translate<SerialsKey> }) {
  const kit = useKitT(ctx.language);
  // The sales this till has rung up and not yet had accepted by the server: their lines say which items can take a serial number.
  const recent = useLoad(async () => ok((await ctx.outbox.all()).filter(isSaleEntry).map((e) => e.sale).reverse().slice(0, 10)), []);
  const [chosen, setChosen] = useState("");
  const [typed, setTyped] = useState("");
  const [productId, setProductId] = useState("");
  const [serial, setSerial] = useState("");
  const [staged, setStaged] = useState<Array<SerialItem & { name: string }>>([]);
  const [problem, setProblem] = useState("");
  const [info, setInfo] = useState("");
  const action = useAction();

  const sales: SaleUploadDto[] = recent.data ?? [];
  const sale = sales.find((s) => s.id === chosen);
  const items = sale ? sale.lines.map((l) => ({ id: l.productId, name: l.name })) : ctx.catalogue.map((c) => ({ id: c.id, name: c.name }));
  const saleId = (typed.trim() || chosen).trim();

  function stage() {
    const text = serial.trim();
    const item = items.find((i) => i.id === productId);
    if (!item) return setProblem(t("record.itemRequired"));
    if (text === "") return setProblem(t("record.serialRequired"));
    if (staged.some((s) => s.productId === item.id && s.serial.toLowerCase() === text.toLowerCase())) return setProblem(t("record.dupe", { serial: text }));
    setProblem("");
    setStaged([...staged, { productId: item.id, serial: text, name: item.name }]);
    setSerial("");
  }

  async function save() {
    if (saleId === "") return setProblem(t("record.saleRequired"));
    if (!isUuid(saleId)) return setProblem(t("record.saleBad"));
    if (staged.length === 0) return setProblem(t("record.nothing"));
    setProblem("");
    setInfo("");
    const r = await action.run(() => recordSerials(ctx.api, caller, saleId, staged.map(({ productId: p, serial: s }) => ({ productId: p, serial: s }))));
    if (r.ok) {
      setInfo(t("record.saved", { count: staged.length }));
      setStaged([]);
    } else if (r.code === "serial.sale_unknown") {
      action.clear();
      setProblem(t("record.wait"));
    }
  }

  return (
    <Stack>
      {sales.length === 0 && !recent.loading ? <Notice>{t("record.noRecent")}</Notice> : (
        <SelectField label={t("record.sale")} value={chosen} onChange={(e) => { setChosen(e.target.value); setProductId(""); }}>
          <option value="">{t("record.saleChoose")}</option>
          {sales.map((s) => <option key={s.id} value={s.id}>{t("record.saleOption", { time: saDateTime(s.soldAt), total: formatRand(s.totalCents) })}</option>)}
        </SelectField>
      )}
      <TextField label={t("record.saleOther")} hint={t("record.saleOtherHint")} value={typed} onChange={(e) => setTyped(e.target.value)} autoComplete="off" />
      <SelectField label={t("record.item")} value={productId} onChange={(e) => setProductId(e.target.value)}>
        <option value="">{t("record.itemChoose")}</option>
        {items.map((i) => <option key={i.id} value={i.id}>{i.name}</option>)}
      </SelectField>
      <TextField label={t("record.serial")} value={serial} onChange={(e) => setSerial(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") stage(); }} autoComplete="off" />
      <Inline><Button variant="secondary" onClick={stage}>{t("record.add")}</Button></Inline>

      <h3>{t("record.staged")}</h3>
      <Table
        empty={t("record.none")}
        rows={staged}
        rowKey={(s) => `${s.productId}-${s.serial}`}
        columns={[
          { header: t("record.colItem"), cell: (s) => s.name },
          { header: t("record.colSerial"), cell: (s) => s.serial },
          { header: "", cell: (s) => <Button variant="quiet" aria-label={t("record.remove", { serial: s.serial })} onClick={() => setStaged(staged.filter((x) => x !== s))}>x</Button> },
        ]}
      />
      {problem ? <Notice kind="danger">{problem}</Notice> : null}
      {info ? <Notice kind="success">{info}</Notice> : null}
      <ErrorNotice error={action.error} t={kit} />
      <Inline><Button onClick={() => void save()} disabled={action.busy}>{t("record.save")}</Button></Inline>
    </Stack>
  );
}

function Search({ ctx, caller, t }: { ctx: TillContext; caller: Caller; t: Translate<SerialsKey> }) {
  const kit = useKitT(ctx.language);
  const [text, setText] = useState("");
  const [problem, setProblem] = useState("");
  const [rows, setRows] = useState<SerialRow[] | null>(null);
  const action = useAction();

  async function search() {
    if (text.trim().length < 3) return setProblem(t("search.short"));
    setProblem("");
    const r = await action.run(() => findSerials(ctx.api, caller, text));
    if (r.ok) setRows(r.value);
  }

  return (
    <Stack>
      <TextField label={t("search.text")} hint={t("search.hint")} value={text} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") void search(); }} error={problem || null} autoComplete="off" />
      <ErrorNotice error={action.error} t={kit} />
      <Inline><Button onClick={() => void search()} disabled={action.busy}>{t("search.button")}</Button></Inline>
      {rows ? (
        <Table
          empty={t("search.none")}
          rows={rows}
          rowKey={(r) => `${r.saleId}-${r.serial}`}
          columns={[
            { header: t("search.colSerial"), cell: (r) => r.serial },
            { header: t("search.colItem"), cell: (r) => r.productName },
            { header: t("search.colSold"), cell: (r) => saDateTime(r.soldAt) },
            { header: t("search.colSale"), cell: (r) => r.saleId.slice(0, 8) },
          ]}
        />
      ) : null}
    </Stack>
  );
}
