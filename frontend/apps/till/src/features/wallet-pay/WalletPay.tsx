import { Button, Card, CheckField, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { emptyBasket, formatRand, newId, queueSale, summariseBasket, toSaleUpload, type Basket } from "@tillknowit/domain";
import { useRef, useState } from "react";
import type { TillContext } from "../../feature";
import { TabGate, useAction, useT, type Caller, type Translate } from "../till-kit/kit";
import { costOf, divisorOf, LinesEditor, lineCents, linesTotal } from "../till-kit/LinesEditor";
import type { TicketLineDto } from "../till-kit/types";
import { messages, type WalletKey } from "./messages";
import { takeWalletPayment, type WalletPaymentResult } from "./walletApi";

/** The server's decline in plain words for the cashier; a code we do not know shows the server's own message. */
export function declineText(t: Translate<WalletKey>, code: string, serverMessage: string): string {
  const key = `decline.${code}`;
  return key in messages.en ? t(key as WalletKey) : serverMessage;
}

export function WalletPay({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <WalletBody ctx={ctx} caller={caller} />}</TabGate>;
}

function WalletBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const [lines, setLines] = useState<TicketLineDto[]>([]);
  const [token, setToken] = useState("");
  const [kind, setKind] = useState<"qr" | "card">("qr");
  const [staff, setStaff] = useState(false);
  const [problem, setProblem] = useState("");
  const [done, setDone] = useState<WalletPaymentResult | null>(null);
  // One sale id per sale: pressing the button again after a lost answer asks about the same payment, so the wallet is never charged twice.
  const saleId = useRef<string | null>(null);
  const action = useAction();

  const total = linesTotal(lines);

  async function charge() {
    if (lines.length === 0) return setProblem(t("pay.noItems"));
    if (token.trim() === "") return setProblem(t("pay.tokenRequired"));
    setProblem("");
    saleId.current ??= newId();
    const items = lines.map((l) => ({ productId: l.productId, categoryCode: null, name: l.name, quantity: divisorOf(l) === 1000 ? 1 : l.quantity, priceCents: divisorOf(l) === 1000 ? lineCents(l) : l.priceCents }));
    const r = await action.run(() => takeWalletPayment(ctx.api, caller, { saleId: saleId.current!, amountCents: total, items, token, kind, useStaffAccount: staff }));
    if (!r.ok) return;

    // The wallet has paid: ring the sale up on this till, paid by wallet with the purchase as its reference, and queue it.
    const now = new Date();
    const basket: Basket = {
      ...emptyBasket,
      entries: lines.map((l) => ({ productId: l.productId, name: l.name, quantity: l.quantity, unitPriceCents: l.priceCents, costCents: costOf(ctx.catalogue, l.productId), unitDivisor: divisorOf(l) })),
    };
    const sale = toSaleUpload(basket, summariseBasket(basket, [], now), [{ method: "wallet", amountCents: total, reference: r.value.purchaseId }], { saleId: saleId.current, staffId: caller.staffId }, now);
    if (sale.ok) {
      await queueSale(ctx.outbox, sale.value, now);
      ctx.nudgeSync();
    }
    setDone(r.value);
    saleId.current = null;
    setLines([]);
    setToken("");
  }

  if (done) {
    const who = t("pay.who", { name: done.holderName, grade: done.grade ? `, ${done.grade}` : "", class: done.className ? ` ${done.className}` : "" });
    return (
      <Card title={t("title")}>
        <Stack>
          <Notice kind="success">
            <Stack>
              <strong>{t("pay.done", { amount: formatRand(done.amountCents), name: done.holderName })}</strong>
              <span>{who}</span>
              <span>{t("pay.check", { name: done.holderName })}</span>
              {done.hasPhoto ? <span>{t("pay.photo")}</span> : null}
              {done.balanceCents !== null ? <span>{t("pay.balance", { amount: formatRand(done.balanceCents) })}</span> : <span>{t("pay.staffNote")}</span>}
              {done.repeated ? <span>{t("pay.repeated")}</span> : <span>{t("pay.queued")}</span>}
            </Stack>
          </Notice>
          <Inline><Button onClick={() => setDone(null)}>{t("pay.again")}</Button></Inline>
        </Stack>
      </Card>
    );
  }

  return (
    <Card title={t("title")}>
      <Stack>
        <p>{t("pay.intro")}</p>
        <LinesEditor catalogue={ctx.catalogue} language={ctx.language} lines={lines} onChange={setLines} />
        <SelectField label={t("pay.kind")} value={kind} onChange={(e) => setKind(e.target.value as "qr" | "card")}>
          <option value="qr">{t("pay.kind.qr")}</option>
          <option value="card">{t("pay.kind.card")}</option>
        </SelectField>
        <TextField label={t("pay.token")} hint={t("pay.tokenHint")} value={token} onChange={(e) => setToken(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") void charge(); }} autoComplete="off" />
        <CheckField label={t("pay.staff")} checked={staff} onChange={(e) => setStaff(e.target.checked)} />
        {problem ? <Notice kind="danger">{problem}</Notice> : null}
        {action.error ? (
          <Notice kind="danger">
            <strong>{t("decline.title")}</strong> {declineText(t, action.error.code, action.error.message)}
          </Notice>
        ) : null}
        <Inline><Button onClick={() => void charge()} disabled={action.busy}>{t("pay.charge", { amount: formatRand(total) })}</Button></Inline>
      </Stack>
    </Card>
  );
}
