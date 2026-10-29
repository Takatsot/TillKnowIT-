import { Badge, Button, Notice, Stack, TextField } from "@tillknowit/design-system";
import { isUuid, newId, type CatalogueItem } from "@tillknowit/domain";
import { useCallback, useEffect, useRef, useState } from "react";
import { ProductPicker, useBusy, useT, wholeNumber, type Translate } from "../../coreKit";
import type { TillContext } from "../../feature";
import { messages, type TransfersKey } from "./messages";

/** A transfer as the server sends it (TransferResponse in ShopCore.Contracts). */
export interface Transfer {
  id: string;
  fromShopId: string;
  toShopId: string;
  status: string;
  sentAt: string;
  closedAt: string | null;
  note: string | null;
  lines: Array<{ productId: string; name: string; quantity: number; receivedQuantity: number | null; shortfall: number }>;
}

type Mode = "incoming" | "sent" | "send";

/** The server's own words for a refusal, except the two we explain better. */
function problemText(t: Translate<TransfersKey>, code: string, message: string): string {
  const key = code as TransfersKey;
  return code === "plan.feature_not_included" || code === "transfer.not_a_branch" ? t(key) : message;
}

/**
 * Moving stock between a shop's own branches: send, receive (with what actually arrived) and cancel. Every call goes to the server and the server does the stock
 * movements, so this tab needs a connection (requiresOnline) and nothing here is kept on the till.
 */
export function CoreTransfers({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  const [mode, setMode] = useState<Mode>("incoming");
  const [transfers, setTransfers] = useState<Transfer[] | null>(null);
  const [message, setMessage] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const caller = ctx.device ? { shopId: ctx.device.shopId, deviceId: ctx.device.deviceId, staffId: ctx.staff.id } : null;

  const load = useCallback(async () => {
    if (!caller) return;
    const result = await ctx.api.get<Transfer[]>(`/shops/${caller.shopId}/stock-transfers`, { deviceId: caller.deviceId, staffId: caller.staffId });
    if (result.ok) setTransfers(result.value);
    else {
      setTransfers([]);
      setMessage({ kind: "danger", text: problemText(t, result.code, result.message) });
    }
  }, [ctx.api, caller?.shopId, caller?.deviceId, caller?.staffId]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!ctx.online || !caller) return <Notice kind="warning">{t("needsOnline")}</Notice>;

  const incoming = (transfers ?? []).filter((x) => x.toShopId === caller.shopId);
  const sent = (transfers ?? []).filter((x) => x.fromShopId === caller.shopId);

  return (
    <Stack>
      <div role="group" aria-label={t("title")} style={{ display: "flex", flexWrap: "wrap", gap: "var(--space-2)" }}>
        {(["incoming", "sent", "send"] as const).map((m) => <Button key={m} variant={mode === m ? "primary" : "secondary"} aria-pressed={mode === m} onClick={() => { setMode(m); setMessage(null); }}>{t(`mode.${m}`)}</Button>)}
      </div>
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      {mode === "incoming" && (transfers === null ? <p>{t("loading")}</p> : incoming.length === 0 ? <p>{t("incoming.empty")}</p> : (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-4)" }}>
          {incoming.map((x) => <li key={x.id}><TransferCard ctx={ctx} t={t} transfer={x} side="incoming" caller={caller} onDone={async (m) => { setMessage(m); await load(); }} /></li>)}
        </ul>
      ))}
      {mode === "sent" && (transfers === null ? <p>{t("loading")}</p> : sent.length === 0 ? <p>{t("sent.empty")}</p> : (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-4)" }}>
          {sent.map((x) => <li key={x.id}><TransferCard ctx={ctx} t={t} transfer={x} side="sent" caller={caller} onDone={async (m) => { setMessage(m); await load(); }} /></li>)}
        </ul>
      ))}
      {mode === "send" && <SendForm ctx={ctx} t={t} caller={caller} onDone={async (m) => { setMessage(m); setMode("sent"); await load(); }} />}
    </Stack>
  );
}

interface Caller {
  shopId: string;
  deviceId: string;
  staffId: string;
}

type Done = (message: { kind: "success" | "danger"; text: string }) => Promise<void>;

function TransferCard({ ctx, t, transfer, side, caller, onDone }: { ctx: TillContext; t: Translate<TransfersKey>; transfer: Transfer; side: "incoming" | "sent"; caller: Caller; onDone: Done }) {
  const [received, setReceived] = useState<Record<string, string>>({});
  const { busy, run } = useBusy();
  const open = transfer.status === "sent";
  const statusKey = `status.${transfer.status}` as TransfersKey;

  async function receive() {
    const lines = transfer.lines.map((l) => ({ productId: l.productId, quantity: wholeNumber(received[l.productId] ?? String(l.quantity)) }));
    if (lines.some((l) => l.quantity === null)) return onDone({ kind: "danger", text: t("quantityInvalid") });
    const everything = lines.every((l, i) => l.quantity === transfer.lines[i]?.quantity);
    // Leaving the lines out means everything came.
    const result = await ctx.api.post(`/shops/${caller.shopId}/stock-transfers/${transfer.id}/receive`, { deviceId: caller.deviceId, staffId: caller.staffId, lines: everything ? undefined : lines });
    await onDone(result.ok ? { kind: "success", text: t("receiveDone") } : { kind: "danger", text: problemText(t, result.code, result.message) });
  }

  async function cancel() {
    const result = await ctx.api.post(`/shops/${caller.shopId}/stock-transfers/${transfer.id}/cancel`, { deviceId: caller.deviceId, staffId: caller.staffId });
    await onDone(result.ok ? { kind: "success", text: t("cancelDone") } : { kind: "danger", text: problemText(t, result.code, result.message) });
  }

  return (
    <article style={{ display: "grid", gap: "var(--space-2)", padding: "var(--space-3)", border: "1px solid var(--color-border)", borderRadius: "var(--radius)" }}>
      <strong>{side === "incoming" ? t("from", { shop: transfer.fromShopId }) : t("to", { shop: transfer.toShopId })}</strong>
      <span>{t("sentAt", { time: new Date(transfer.sentAt).toLocaleString("en-ZA") })} <Badge>{statusKey in messages.en ? t(statusKey) : transfer.status}</Badge></span>
      {transfer.note && <span>{t("note", { note: transfer.note })}</span>}
      <ul style={{ margin: 0, paddingLeft: "var(--space-4)" }}>
        {transfer.lines.map((l) => (
          <li key={l.productId}>{l.receivedQuantity !== null ? t("lineReceived", { name: l.name, received: l.receivedQuantity, quantity: l.quantity, short: l.shortfall }) : t("line", { name: l.name, quantity: l.quantity })}</li>
        ))}
      </ul>
      {open && side === "incoming" && (
        <>
          {transfer.lines.map((l) => (
            <TextField key={l.productId} label={t("received", { name: l.name })} inputMode="numeric" value={received[l.productId] ?? String(l.quantity)} onChange={(e) => setReceived({ ...received, [l.productId]: e.target.value })} />
          ))}
          <Button disabled={busy} onClick={() => void run(receive)}>{t("receive")}</Button>
        </>
      )}
      {open && side === "sent" && <Button variant="secondary" disabled={busy} onClick={() => void run(cancel)}>{t("cancel")}</Button>}
    </article>
  );
}

function SendForm({ ctx, t, caller, onDone }: { ctx: TillContext; t: Translate<TransfersKey>; caller: Caller; onDone: Done }) {
  const [toShop, setToShop] = useState("");
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<Array<{ item: CatalogueItem; quantity: string }>>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const { busy, run } = useBusy();
  // The id stays the same until the server has the transfer, so pressing the button again after a lost answer is one transfer, not two.
  const id = useRef<string | null>(null);

  function add(item: CatalogueItem) {
    setProblem(null);
    setLines((all) => (all.some((l) => l.item.id === item.id) ? all.map((l) => (l.item.id === item.id ? { ...l, quantity: String((wholeNumber(l.quantity) ?? 0) + 1) } : l)) : [...all, { item, quantity: "1" }]));
  }

  async function submit() {
    const to = toShop.trim().toLowerCase();
    if (!isUuid(to)) return setProblem(t("toShopInvalid"));
    if (to === caller.shopId.toLowerCase()) return setProblem(t("sameShop"));
    const parsed = lines.map((l) => ({ productId: l.item.id, quantity: wholeNumber(l.quantity), toProductId: null }));
    if (parsed.length === 0 || parsed.some((l) => l.quantity === null || l.quantity <= 0)) return setProblem(t("quantityInvalid"));
    id.current ??= newId(Date.now());
    const result = await ctx.api.post(`/shops/${caller.shopId}/stock-transfers`, { deviceId: caller.deviceId, staffId: caller.staffId, id: id.current, toShopId: to, lines: parsed, note: note.trim() || null });
    if (!result.ok) return setProblem(problemText(t, result.code, result.message));
    id.current = null;
    setLines([]);
    setNote("");
    setToShop("");
    setProblem(null);
    await onDone({ kind: "success", text: t("sendDone") });
  }

  return (
    <Stack>
      <TextField label={t("toShop")} autoComplete="off" value={toShop} onChange={(e) => setToShop(e.target.value)} />
      <ProductPicker catalogue={ctx.catalogue} label={t("find")} notFound={t("notFound")} onPick={add} />
      {lines.length === 0 ? <p>{t("empty")}</p> : (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-3)" }}>
          {lines.map((l) => (
            <li key={l.item.id} style={{ display: "grid", gap: "var(--space-2)", gridTemplateColumns: "1fr auto" }}>
              <TextField label={t("quantity", { name: l.item.name })} inputMode="numeric" value={l.quantity} onChange={(e) => setLines(lines.map((x) => (x.item.id === l.item.id ? { ...x, quantity: e.target.value } : x)))} />
              <Button variant="quiet" aria-label={t("remove", { name: l.item.name })} onClick={() => setLines(lines.filter((x) => x.item.id !== l.item.id))}>✕</Button>
            </li>
          ))}
        </ul>
      )}
      <TextField label={t("noteField")} value={note} onChange={(e) => setNote(e.target.value)} />
      {problem && <Notice kind="danger">{problem}</Notice>}
      <Button disabled={busy || lines.length === 0} onClick={() => void run(submit)}>{t("send")}</Button>
    </Stack>
  );
}
