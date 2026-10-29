import { Badge, Button, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { countAdjustment, formatRand, isSaleEntry, newId, parseRandToCents, queueBuilt, receiptMovements, stockMovementRecord, type CatalogueItem } from "@tillknowit/domain";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ProductPicker, useBusy, useT, wholeNumber, type Translate } from "../../coreKit";
import type { TillContext } from "../../feature";
import { messages, type StockKey } from "./messages";
import { cachedStock, fetchStock, type StockCopy } from "./stockData";

type Mode = "list" | "receive" | "count" | "writeOff";
const modes: ReadonlyArray<{ id: Mode; label: StockKey }> = [
  { id: "list", label: "mode.list" }, { id: "receive", label: "mode.receive" }, { id: "count", label: "mode.count" }, { id: "writeOff", label: "mode.writeOff" },
];
const countReasons = ["counted", "expired", "damaged", "missing", "own_use"] as const;
const lossReasons = ["expired", "damaged", "missing", "own_use"] as const;

/**
 * Stock: the list, receiving a delivery, counting a product and writing off what is lost. Receiving, counting and writing off are stock movements: they are
 * saved on the till and sent through the outbox like a sale, so they work with no connection. The figures are as of the last time the till was online.
 */
export function CoreStock({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  const [mode, setMode] = useState<Mode>("list");
  const [copy, setCopy] = useState<StockCopy | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The figures are the server's (as of the last time the till was online) plus the movements still waiting in the queue, which the server has not counted yet.
  const refresh = useCallback(async () => {
    let next: StockCopy | null;
    if (!ctx.online) next = await cachedStock(ctx.kv);
    else {
      const result = await fetchStock(ctx.api, ctx.device, ctx.kv, new Date());
      next = result.copy;
      setError(result.error);
    }
    const waiting = new Map<string, number>();
    for (const e of await ctx.outbox.all()) {
      if (isSaleEntry(e) || e.kind !== "stock_movement" || e.state !== "pending") continue;
      const m = e.record as { productId: string; quantity: number };
      waiting.set(m.productId, (waiting.get(m.productId) ?? 0) + m.quantity);
    }
    setCopy(next && { ...next, rows: next.rows.map((r) => ({ ...r, onHand: r.onHand + (waiting.get(r.productId) ?? 0) })) });
  }, [ctx.api, ctx.device, ctx.kv, ctx.online, ctx.outbox]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const onHandOf = (productId: string): number | null => copy?.rows.find((r) => r.productId === productId)?.onHand ?? null;

  return (
    <Stack>
      <div role="group" aria-label={t("title")} style={{ display: "flex", flexWrap: "wrap", gap: "var(--space-2)" }}>
        {modes.map((m) => <Button key={m.id} variant={mode === m.id ? "primary" : "secondary"} aria-pressed={mode === m.id} onClick={() => setMode(m.id)}>{t(m.label)}</Button>)}
      </div>
      {error && <Notice kind="warning">{error}</Notice>}
      {mode === "list" && <StockList t={t} copy={copy} online={ctx.online} onRefresh={() => void refresh()} />}
      {mode === "receive" && <Receive ctx={ctx} t={t} onSaved={refresh} />}
      {mode === "count" && <Count ctx={ctx} t={t} onHandOf={onHandOf} onSaved={refresh} />}
      {mode === "writeOff" && <WriteOff ctx={ctx} t={t} onSaved={refresh} />}
    </Stack>
  );
}

function StockList({ t, copy, online, onRefresh }: { t: Translate<StockKey>; copy: StockCopy | null; online: boolean; onRefresh: () => void }) {
  if (!copy || copy.rows.length === 0) return <p>{t("list.empty")}</p>;
  const rows = [...copy.rows].sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
  return (
    <Stack>
      <p>{online ? t("list.asOf", { time: new Date(copy.asOf).toLocaleString("en-ZA") }) : t("list.offlineCopy")}</p>
      <Table
        rows={rows} rowKey={(r) => r.productId}
        columns={[
          { header: t("list.name"), cell: (r) => r.name },
          { header: t("list.onHand"), align: "right", cell: (r) => r.onHand },
          { header: t("list.flags"), cell: (r) => r.flags.map((f) => <Badge key={f}>{flagText(t, f)}</Badge>) },
        ]}
      />
      <Button variant="quiet" onClick={onRefresh} disabled={!online}>{t("list.refresh")}</Button>
    </Stack>
  );
}

const flagText = (t: Translate<StockKey>, flag: string): string => {
  const key = `flag.${flag}` as StockKey;
  return key in messages.en ? t(key) : flag;
};

/** Checks the movements, queues them, and tells the sync runner. Returns the first problem in words, or null when all were saved. */
async function save(ctx: TillContext, drafts: Array<ReturnType<typeof stockMovementRecord>>): Promise<string | null> {
  for (const d of drafts) if (!d.ok) return d.message;
  const now = new Date();
  for (const d of drafts) {
    if (!d.ok) continue;
    await queueBuilt(ctx.outbox, "stock_movement", d, now);
  }
  ctx.nudgeSync();
  return null;
}

const unitLabel = (t: Translate<StockKey>, item: CatalogueItem) => (item.soldByWeight ? t("grams") : t("units"));

// ---------------------------------------------------------------- receive

interface ReceiveLine {
  item: CatalogueItem;
  quantity: string;
  cost: string;
}

function Receive({ ctx, t, onSaved }: { ctx: TillContext; t: Translate<StockKey>; onSaved: () => Promise<void> }) {
  const [lines, setLines] = useState<ReceiveLine[]>([]);
  const [supplier, setSupplier] = useState("");
  const [invoice, setInvoice] = useState("");
  const [message, setMessage] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const { busy, run } = useBusy();

  function add(item: CatalogueItem) {
    setMessage(null);
    setLines((all) => {
      const at = all.findIndex((l) => l.item.id === item.id);
      return at >= 0 ? all.map((l, i) => (i === at ? { ...l, quantity: String((wholeNumber(l.quantity) ?? 0) + 1) } : l)) : [...all, { item, quantity: "1", cost: (item.costCents / 100).toFixed(2) }];
    });
  }

  const change = (id: string, part: Partial<ReceiveLine>) => setLines((all) => all.map((l) => (l.item.id === id ? { ...l, ...part } : l)));

  async function submit() {
    const parsed: Array<{ productId: string; quantity: number; unitCostCents: number }> = [];
    for (const l of lines) {
      const quantity = wholeNumber(l.quantity);
      const unitCostCents = parseRandToCents(l.cost);
      if (quantity === null || quantity <= 0) return setMessage({ kind: "danger", text: t("receive.quantityInvalid") });
      if (unitCostCents === null) return setMessage({ kind: "danger", text: t("receive.costInvalid") });
      parsed.push({ productId: l.item.id, quantity, unitCostCents });
    }
    const receipt = receiptMovements(parsed, supplier, invoice);
    if (!receipt.ok) return setMessage({ kind: "danger", text: receipt.message });
    const now = new Date();
    const problem = await run(() => save(ctx, receipt.movements.map((m) => stockMovementRecord(m, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now }))));
    if (problem) return setMessage({ kind: "danger", text: t("saveFailed", { message: problem }) });
    setMessage({ kind: "success", text: t("receive.saved", { count: lines.length }) });
    setLines([]);
    setSupplier("");
    setInvoice("");
    await onSaved();
  }

  return (
    <Stack>
      <ProductPicker catalogue={ctx.catalogue} label={t("find")} notFound={t("notFound")} onPick={add} />
      {lines.length === 0 ? <p>{t("receive.empty")}</p> : (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-3)" }}>
          {lines.map((l) => (
            <li key={l.item.id} style={{ display: "grid", gap: "var(--space-2)", gridTemplateColumns: "1fr 1fr auto" }}>
              <TextField label={t("receive.quantity", { name: l.item.name })} inputMode="numeric" hint={unitLabel(t, l.item)} value={l.quantity} onChange={(e) => change(l.item.id, { quantity: e.target.value })} />
              <TextField label={t("receive.cost", { name: l.item.name })} inputMode="decimal" value={l.cost} onChange={(e) => change(l.item.id, { cost: e.target.value })} />
              <Button variant="quiet" aria-label={t("receive.remove", { name: l.item.name })} onClick={() => setLines((all) => all.filter((x) => x.item.id !== l.item.id))}>✕</Button>
            </li>
          ))}
        </ul>
      )}
      <TextField label={t("receive.supplier")} value={supplier} onChange={(e) => setSupplier(e.target.value)} />
      <TextField label={t("receive.invoice")} value={invoice} onChange={(e) => setInvoice(e.target.value)} />
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      <Button disabled={lines.length === 0 || busy} onClick={() => void submit()}>{t("receive.save")}</Button>
    </Stack>
  );
}

// ---------------------------------------------------------------- count

function Count({ ctx, t, onHandOf, onSaved }: { ctx: TillContext; t: Translate<StockKey>; onHandOf: (id: string) => number | null; onSaved: () => Promise<void> }) {
  const [item, setItem] = useState<CatalogueItem | null>(null);
  const [counted, setCounted] = useState("");
  const [reason, setReason] = useState<string>("counted");
  const [message, setMessage] = useState<{ kind: "success" | "info" | "danger"; text: string } | null>(null);
  const { busy, run } = useBusy();
  const onHand = item ? onHandOf(item.id) : null;

  async function submit() {
    if (!item || onHand === null) return;
    const number = wholeNumber(counted);
    if (number === null) return setMessage({ kind: "danger", text: t("count.invalid") });
    const adjustment = countAdjustment(onHand, number, reason);
    if (!adjustment) return setMessage({ kind: "info", text: t("count.same") });
    const now = new Date();
    const problem = await run(() => save(ctx, [stockMovementRecord({ productId: item.id, type: adjustment.type, quantity: adjustment.quantity, reasonCode: reason }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now })]));
    if (problem) return setMessage({ kind: "danger", text: t("saveFailed", { message: problem }) });
    setMessage({ kind: "success", text: t("count.saved", { name: item.name, change: adjustment.quantity > 0 ? `+${adjustment.quantity}` : String(adjustment.quantity), now: number }) });
    setItem(null);
    setCounted("");
    await onSaved();
  }

  if (!item) {
    return (
      <Stack>
        <ProductPicker catalogue={ctx.catalogue} label={t("find")} notFound={t("notFound")} onPick={(p) => { setItem(p); setMessage(null); }} />
        {message && <Notice kind={message.kind}>{message.text}</Notice>}
      </Stack>
    );
  }

  return (
    <Stack>
      <h2>{item.name}</h2>
      {onHand === null ? <Notice kind="warning">{t("count.unknown", { name: item.name })}</Notice> : <p>{t("count.onHand", { count: onHand, name: item.name })}</p>}
      <TextField label={t("count.counted")} inputMode="numeric" hint={unitLabel(t, item)} value={counted} onChange={(e) => setCounted(e.target.value)} />
      <ReasonSelect t={t} label={t("count.reason")} value={reason} onChange={setReason} reasons={countReasons} />
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      <div style={{ display: "flex", gap: "var(--space-3)" }}>
        <Button variant="quiet" onClick={() => { setItem(null); setCounted(""); }}>{t("clear")}</Button>
        <Button disabled={onHand === null || counted.trim() === "" || busy} onClick={() => void submit()}>{t("count.save")}</Button>
      </div>
    </Stack>
  );
}

function ReasonSelect({ t, label, value, onChange, reasons }: { t: Translate<StockKey>; label: string; value: string; onChange: (v: string) => void; reasons: readonly string[] }) {
  return (
    <SelectField label={label} value={value} onChange={(e) => onChange(e.target.value)}>
      {reasons.map((r) => <option key={r} value={r}>{t(`reason.${r}` as StockKey)}</option>)}
    </SelectField>
  );
}

// ---------------------------------------------------------------- write off

function WriteOff({ ctx, t, onSaved }: { ctx: TillContext; t: Translate<StockKey>; onSaved: () => Promise<void> }) {
  const [item, setItem] = useState<CatalogueItem | null>(null);
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState<string>("damaged");
  const [message, setMessage] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const { busy, run } = useBusy();
  const reasons = useMemo(() => lossReasons, []);

  async function submit() {
    if (!item) return;
    const number = wholeNumber(quantity);
    if (number === null || number <= 0) return setMessage({ kind: "danger", text: t("writeOff.invalid") });
    const now = new Date();
    const problem = await run(() => save(ctx, [stockMovementRecord({ productId: item.id, type: "write_off", quantity: -number, reasonCode: reason }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now })]));
    if (problem) return setMessage({ kind: "danger", text: t("saveFailed", { message: problem }) });
    setMessage({ kind: "success", text: t("writeOff.saved", { count: number, name: item.name }) });
    setItem(null);
    setQuantity("");
    await onSaved();
  }

  if (!item) {
    return (
      <Stack>
        <ProductPicker catalogue={ctx.catalogue} label={t("find")} notFound={t("notFound")} onPick={(p) => { setItem(p); setMessage(null); }} />
        {message && <Notice kind={message.kind}>{message.text}</Notice>}
      </Stack>
    );
  }

  return (
    <Stack>
      <h2>{item.name} <small>{formatRand(item.costCents)}</small></h2>
      <TextField label={t("writeOff.quantity")} inputMode="numeric" hint={unitLabel(t, item)} value={quantity} onChange={(e) => setQuantity(e.target.value)} />
      <ReasonSelect t={t} label={t("writeOff.reason")} value={reason} onChange={setReason} reasons={reasons} />
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      <div style={{ display: "flex", gap: "var(--space-3)" }}>
        <Button variant="quiet" onClick={() => { setItem(null); setQuantity(""); }}>{t("clear")}</Button>
        <Button disabled={quantity.trim() === "" || busy} onClick={() => void submit()}>{t("writeOff.save")}</Button>
      </div>
    </Stack>
  );
}
