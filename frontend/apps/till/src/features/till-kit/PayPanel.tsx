import { Button, Card, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { emptyBasket, formatRand, newId, parseRandToCents, queueSale, summariseBasket, toSaleUpload, type Basket, type Payment } from "@tillknowit/domain";
import { useRef, useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, useAction, useKitT, type Caller } from "./kit";
import { costOf, divisorOf } from "./LinesEditor";
import type { TicketResponse } from "./types";

interface Props {
  ctx: TillContext;
  caller: Caller;
  ticket: Pick<TicketResponse, "id" | "label" | "lines">;
  onPaid: (text: string) => void;
  onBack: () => void;
}

/**
 * Takes payment for a ticket. The till rings the sale up itself (as the sell screen does), queues it, and then tells the server the ticket is closed by that
 * sale. The sale id is kept until the ticket is closed, so pressing the button again after a failure never makes a second sale.
 */
export function PayPanel({ ctx, caller, ticket, onPaid, onBack }: Props) {
  const kit = useKitT(ctx.language);
  const t = kit;
  const [method, setMethod] = useState<"cash" | "card" | "qr">("cash");
  const [given, setGiven] = useState("");
  const [message, setMessage] = useState("");
  const saleId = useRef<string | null>(null);
  const action = useAction();

  const basket: Basket = {
    ...emptyBasket,
    entries: ticket.lines.map((l) => ({
      productId: l.productId, name: l.name, quantity: l.quantity, unitPriceCents: l.priceCents, costCents: costOf(ctx.catalogue, l.productId), unitDivisor: divisorOf(l),
    })),
  };
  // The ticket's prices are already agreed, so no promotion is applied here: the sale is exactly the ticket total.
  const summary = summariseBasket(basket, [], new Date());
  const enabled = ctx.features.paymentMethods().filter((m): m is "cash" | "card" | "qr" => m === "cash" || m === "card" || m === "qr");

  async function confirm() {
    setMessage("");
    const now = new Date();
    const tendered = method === "cash" ? (given.trim() === "" ? summary.dueCents : parseRandToCents(given)) : null;
    if (method === "cash" && (tendered === null || tendered < summary.dueCents)) return setMessage(t("pay.cashShort"));
    const payment: Payment = method === "cash" ? { method, amountCents: summary.dueCents, tenderedCents: tendered ?? summary.dueCents } : { method, amountCents: summary.dueCents };
    saleId.current ??= newId(now.getTime());
    const sale = toSaleUpload(basket, summary, [payment], { saleId: saleId.current, staffId: caller.staffId }, now);
    if (!sale.ok) return setMessage(sale.message);

    await queueSale(ctx.outbox, sale.value, now);
    ctx.nudgeSync();
    const closed = await action.run(() => ctx.api.post<TicketResponse>(`/shops/${caller.shopId}/tickets/${ticket.id}/close`, { deviceId: caller.deviceId, staffId: caller.staffId, saleId: saleId.current }));
    if (!closed.ok) return setMessage(t("pay.retry"));
    if (method === "cash") {
      try {
        await ctx.hardware.drawer?.open();
      } catch {
        // A drawer that does not open must not undo a paid sale; the cashier can open it by hand.
      }
    }
    const change = sale.value.changeCents ?? 0;
    onPaid(t("pay.done", { amount: formatRand(summary.dueCents) }) + (change > 0 ? ` ${t("pay.change", { amount: formatRand(change) })}` : ""));
  }

  const label = (m: "cash" | "card" | "qr") => t(m === "cash" ? "pay.cash" : m === "card" ? "pay.card" : "pay.qr");

  return (
    <Card title={t("pay.title", { label: ticket.label })}>
      <Stack>
        <strong>{t("pay.due", { amount: formatRand(summary.dueCents) })}</strong>
        <SelectField label={t("pay.method")} value={method} onChange={(e) => setMethod(e.target.value as "cash" | "card" | "qr")}>
          {(enabled.length > 0 ? enabled : (["cash"] as const)).map((m) => <option key={m} value={m}>{label(m)}</option>)}
        </SelectField>
        {method === "cash" ? <TextField label={t("pay.given")} inputMode="decimal" value={given} onChange={(e) => setGiven(e.target.value)} /> : null}
        {message ? <Notice kind="danger">{message}</Notice> : null}
        <ErrorNotice error={action.error && !message ? action.error : null} t={kit} />
        <Inline>
          <Button onClick={() => void confirm()} disabled={action.busy}>{t("pay.confirm", { amount: formatRand(summary.dueCents) })}</Button>
          <Button variant="quiet" onClick={onBack}>{t("pay.back")}</Button>
        </Inline>
      </Stack>
    </Card>
  );
}
