import { formatDateTime, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import type { Branch } from "./Branches";
import { messages } from "./messages";

export interface TransferLine { productId: string; name: string; quantity: number; receivedQuantity: number | null; shortfall: number }
export interface Transfer {
  id: string; fromShopId: string; toShopId: string; status: string; sentAt: string; closedAt: string | null; note: string | null; lines: TransferLine[];
}
interface PulledProduct { id: string; name: string; isActive: boolean }
interface ProductPull { products: PulledProduct[] }

/**
 * Stock transfers between the head shop and its branches.
 * The server wants a deviceId and staffId on every transfer call (they are the till and the staff member). Until sign-in exists the session's deviceId and
 * personId stand in for them.
 */
export function Transfers() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId, deviceId } = useSession();
  const [filter, setFilter] = useState("");
  const who = { deviceId, staffId: personId };

  const list = useLoad(() => api.get<Transfer[]>(`/shops/${shopId}/stock-transfers`, { ...who, status: filter }), [shopId, deviceId, personId, filter]);
  const branches = useLoad(() => api.get<Branch[]>(`/organisations/${shopId}/branches`, { callerPersonId: personId }), [shopId, personId]);
  const products = useLoad(() => api.get<ProductPull>(`/shops/${shopId}/sync/products`, { deviceId }), [shopId, deviceId]);

  const [message, setMessage] = useState("");
  const [receiving, setReceiving] = useState<string | null>(null);
  const shopName = (id: string) => branches.data?.find((b) => b.shopId === id)?.name ?? id;
  const statusLabel = (s: string) => (s === "Sent" ? t("trSent") : s === "Received" ? t("trReceived") : s === "Cancelled" ? t("trCancelled") : s);

  const cancel = useAction((id: string) => api.post<Transfer>(`/shops/${shopId}/stock-transfers/${id}/cancel`, who));
  async function doCancel(id: string) {
    setMessage("");
    const r = await cancel.run(id);
    if (r.ok) {
      setMessage(t("trCancelledDone"));
      list.reload();
    }
  }

  return (
    <Stack>
      <Card title={t("trHeading")}>
        <Stack>
          <SelectField label={t("trFilter")} value={filter} onChange={(e) => setFilter(e.target.value)}>
            <option value="">{t("trAll")}</option>
            <option value="Sent">{t("trSent")}</option>
            <option value="Received">{t("trReceived")}</option>
            <option value="Cancelled">{t("trCancelled")}</option>
          </SelectField>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {cancel.error && <Notice kind="danger">{cancel.error.message}</Notice>}
          {message && <Notice kind="success">{message}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(x) => x.id}
              empty={t("trEmpty")}
              columns={[
                { header: t("trFrom"), cell: (x) => shopName(x.fromShopId) },
                { header: t("trTo"), cell: (x) => shopName(x.toShopId) },
                { header: t("trStatus"), cell: (x) => <Badge>{statusLabel(x.status)}</Badge> },
                { header: t("trSentAt"), cell: (x) => formatDateTime(x.sentAt) },
                {
                  header: t("trLines"),
                  cell: (x) => (
                    <ul>
                      {x.lines.map((l) => (
                        <li key={l.productId}>
                          {l.name}: {l.receivedQuantity === null ? t("trSentQty", { n: l.quantity }) : `${l.receivedQuantity} / ${l.quantity}`}
                          {l.receivedQuantity !== null && (l.shortfall > 0 ? ` (${t("trShort", { n: l.shortfall })})` : ` (${t("trAllArrived")})`)}
                        </li>
                      ))}
                    </ul>
                  ),
                },
                {
                  header: t("trActions"),
                  cell: (x) => x.status !== "Sent" ? "" : (
                    <Inline>
                      {x.toShopId === shopId && <Button variant="secondary" onClick={() => setReceiving(x.id)}>{t("trReceive")}</Button>}
                      {x.fromShopId === shopId && <Button variant="quiet" disabled={cancel.busy} onClick={() => void doCancel(x.id)}>{t("trCancel")}</Button>}
                    </Inline>
                  ),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      {receiving && list.data?.find((x) => x.id === receiving) && (
        <ReceiveForm
          transfer={list.data.find((x) => x.id === receiving)!}
          onDone={() => { setReceiving(null); setMessage(t("trReceivedDone")); list.reload(); }}
        />
      )}
      <SendForm
        branches={(branches.data ?? []).filter((b) => b.shopId !== shopId)}
        products={(products.data?.products ?? []).filter((p) => p.isActive)}
        productsFailed={products.error !== null || (!products.loading && (products.data?.products.length ?? 0) === 0)}
        productsError={products.error?.message ?? null}
        onSent={() => { setMessage(t("trSentDone")); list.reload(); }}
      />
    </Stack>
  );
}

function ReceiveForm({ transfer, onDone }: { transfer: Transfer; onDone: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId, deviceId } = useSession();
  const [qty, setQty] = useState<Record<string, string>>(() => Object.fromEntries(transfer.lines.map((l) => [l.productId, String(l.quantity)])));
  const parsed = transfer.lines.map((l) => {
    const raw = qty[l.productId] ?? "";
    const n = Number(raw);
    return { line: l, n, valid: raw.trim() !== "" && Number.isInteger(n) && n >= 0 && n <= l.quantity };
  });
  const allValid = parsed.every((p) => p.valid);
  const receive = useAction(() => api.post<Transfer>(`/shops/${shopId}/stock-transfers/${transfer.id}/receive`, {
    deviceId, staffId: personId, lines: parsed.map((p) => ({ productId: p.line.productId, quantity: p.n })),
  }));

  async function submit() {
    const r = await receive.run();
    if (r.ok) onDone();
  }

  return (
    <Card title={t("trReceiveHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          {parsed.map((p) => (
            <Inline key={p.line.productId}>
              <TextField
                label={t("trArrived", { name: p.line.name })}
                hint={t("trSentQty", { n: p.line.quantity })}
                error={p.valid ? null : t("trBadQuantity", { max: p.line.quantity })}
                inputMode="numeric"
                value={qty[p.line.productId] ?? ""}
                onChange={(e) => setQty({ ...qty, [p.line.productId]: e.target.value })}
              />
              {p.valid && p.line.quantity - p.n > 0 && <Badge>{t("trShortNow", { n: p.line.quantity - p.n })}</Badge>}
            </Inline>
          ))}
          {receive.error && <Notice kind="danger">{receive.error.message}</Notice>}
          <div><Button type="submit" disabled={receive.busy || !allValid}>{t("trConfirmReceive")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

interface DraftLine { productId: string; name: string; quantity: number }

function SendForm({ branches, products, productsFailed, productsError, onSent }: {
  branches: Branch[]; products: PulledProduct[]; productsFailed: boolean; productsError: string | null; onSent: () => void;
}) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId, deviceId } = useSession();
  const [to, setTo] = useState("");
  const [product, setProduct] = useState("");
  const [quantity, setQuantity] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [note, setNote] = useState("");
  const qtyNumber = Number(quantity);
  const qtyValid = quantity.trim() !== "" && Number.isInteger(qtyNumber) && qtyNumber > 0;

  const send = useAction(() => api.post<Transfer>(`/shops/${shopId}/stock-transfers`, {
    deviceId, staffId: personId, id: crypto.randomUUID(), toShopId: to,
    lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, toProductId: null })),
    note: note.trim() === "" ? null : note.trim(),
  }));

  function addLine() {
    const p = products.find((x) => x.id === product);
    if (!p || !qtyValid) return;
    setLines((ls) => [...ls.filter((l) => l.productId !== p.id), { productId: p.id, name: p.name, quantity: qtyNumber }]);
    setProduct("");
    setQuantity("");
  }

  async function submit() {
    const r = await send.run();
    if (r.ok) {
      setLines([]);
      setNote("");
      setTo("");
      onSent();
    }
  }

  return (
    <Card title={t("trSendHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Notice kind="info">{t("trStandIn")}</Notice>
          {branches.length === 0 && <p>{t("trNoBranches")}</p>}
          {productsError ? <Notice kind="danger">{productsError}</Notice> : productsFailed && <p>{t("trNoProducts")}</p>}
          <SelectField label={t("trDestination")} value={to} onChange={(e) => setTo(e.target.value)}>
            <option value="">{t("trChoose")}</option>
            {branches.map((b) => <option key={b.shopId} value={b.shopId}>{b.name}</option>)}
          </SelectField>
          <Inline>
            <SelectField label={t("trProduct")} value={product} onChange={(e) => setProduct(e.target.value)}>
              <option value="">{t("trChoose")}</option>
              {products.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </SelectField>
            <TextField label={t("trQuantity")} inputMode="numeric" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
            <Button variant="secondary" disabled={product === "" || !qtyValid} onClick={addLine}>{t("trAddLine")}</Button>
          </Inline>
          {lines.length > 0 && (
            <ul>
              {lines.map((l) => (
                <li key={l.productId}>
                  {l.name} x {l.quantity}{" "}
                  <Button variant="quiet" aria-label={t("trRemoveLine", { name: l.name })} onClick={() => setLines((ls) => ls.filter((x) => x.productId !== l.productId))}>x</Button>
                </li>
              ))}
            </ul>
          )}
          <TextField label={t("trNote")} value={note} onChange={(e) => setNote(e.target.value)} />
          {send.error && <Notice kind="danger">{send.error.message}</Notice>}
          <div><Button type="submit" disabled={send.busy || to === "" || lines.length === 0}>{t("trSend")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}
