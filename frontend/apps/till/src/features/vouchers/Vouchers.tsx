import { Button, Card, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { formatRand, parseRandToCents } from "@tillknowit/domain";
import { useRef, useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, TabGate, useAction, useKitT, useT, type Caller, type Translate } from "../till-kit/kit";
import type { VoucherIssued, VoucherResponse } from "../till-kit/types";
import { messages, type VouchersKey } from "./messages";
import { checkVoucher, newIssueReference, newRedemptionReference, redeemVoucher, sellVoucher } from "./voucherApi";

type Mode = "sell" | "check" | "redeem";
const payMethods = ["cash", "card", "qr"] as const;

export function Vouchers({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <VouchersBody ctx={ctx} caller={caller} />}</TabGate>;
}

function VouchersBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const [mode, setMode] = useState<Mode>("sell");
  return (
    <Card title={t("title")}>
      <Stack>
        <Inline>
          {(["sell", "check", "redeem"] as const).map((m) => (
            <Button key={m} variant={mode === m ? "primary" : "secondary"} aria-pressed={mode === m} onClick={() => setMode(m)}>{t(`tab.${m}`)}</Button>
          ))}
        </Inline>
        {mode === "sell" ? <Sell ctx={ctx} caller={caller} t={t} /> : mode === "check" ? <Check ctx={ctx} caller={caller} t={t} /> : <Redeem ctx={ctx} caller={caller} t={t} />}
      </Stack>
    </Card>
  );
}

function Sell({ ctx, caller, t }: { ctx: TillContext; caller: Caller; t: Translate<VouchersKey> }) {
  const kit = useKitT(ctx.language);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<(typeof payMethods)[number]>("cash");
  const [problem, setProblem] = useState("");
  const [issued, setIssued] = useState<VoucherIssued | null>(null);
  const reference = useRef(newIssueReference());
  const action = useAction();

  async function sell() {
    const cents = parseRandToCents(amount);
    if (cents === null || cents <= 0) return setProblem(t("sell.invalid"));
    setProblem("");
    // The same reference is sent again after a failure, so a lost answer can never sell two vouchers.
    const r = await action.run(() => sellVoucher(ctx.api, caller, { amountCents: cents, paidWith: method, reference: reference.current }));
    if (r.ok) {
      setIssued(r.value);
      setAmount("");
      reference.current = newIssueReference();
    }
  }

  if (issued) {
    return (
      <Stack>
        <Notice kind="warning">{t("sell.codeWarning")}</Notice>
        <p>{t("sell.codeTitle")}</p>
        <strong style={{ fontSize: "var(--font-size-xlarge)", letterSpacing: "0.1em" }} aria-label={t("sell.codeTitle")}>{issued.code}</strong>
        <p>{t("sell.codeValue", { amount: formatRand(issued.balanceCents), expiry: issued.expiresOn ? t("sell.expires", { date: issued.expiresOn }) : t("sell.noExpiry") })}</p>
        <Inline><Button onClick={() => setIssued(null)}>{t("sell.done")}</Button></Inline>
      </Stack>
    );
  }
  return (
    <Stack>
      <TextField label={t("sell.amount")} hint={t("sell.amountHint")} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} error={problem || null} />
      <SelectField label={t("sell.paidWith")} value={method} onChange={(e) => setMethod(e.target.value as (typeof payMethods)[number])}>
        {payMethods.map((m) => <option key={m} value={m}>{t(`sell.method.${m}`)}</option>)}
      </SelectField>
      <ErrorNotice error={action.error} t={kit} />
      <Inline><Button onClick={() => void sell()} disabled={action.busy}>{t("sell.button")}</Button></Inline>
    </Stack>
  );
}

function Check({ ctx, caller, t }: { ctx: TillContext; caller: Caller; t: Translate<VouchersKey> }) {
  const kit = useKitT(ctx.language);
  const [code, setCode] = useState("");
  const [problem, setProblem] = useState("");
  const [found, setFound] = useState<VoucherResponse | null>(null);
  const action = useAction();

  async function check() {
    if (code.trim() === "") return setProblem(t("code.required"));
    setProblem("");
    setFound(null);
    const r = await action.run(() => checkVoucher(ctx.api, caller, code));
    if (r.ok) setFound(r.value);
  }

  return (
    <Stack>
      <TextField label={t("check.code")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" error={problem || null} />
      <ErrorNotice error={action.error} t={kit} />
      <Inline><Button onClick={() => void check()} disabled={action.busy}>{t("check.button")}</Button></Inline>
      {found ? (
        <Notice kind={found.isVoid || found.isExpired ? "warning" : "success"}>
          <Stack>
            <span>{t("check.balance", { last4: found.codeLast4, amount: formatRand(found.balanceCents) })}</span>
            {found.expiresOn ? <span>{t("check.expires", { date: found.expiresOn })}</span> : null}
            {found.isVoid ? <span>{t("check.void")}</span> : null}
            {found.isExpired ? <span>{t("check.expired")}</span> : null}
          </Stack>
        </Notice>
      ) : null}
    </Stack>
  );
}

function Redeem({ ctx, caller, t }: { ctx: TillContext; caller: Caller; t: Translate<VouchersKey> }) {
  const kit = useKitT(ctx.language);
  const [code, setCode] = useState("");
  const [amount, setAmount] = useState("");
  const [problem, setProblem] = useState("");
  const [done, setDone] = useState<{ reference: string; amountCents: number; balanceCents: number } | null>(null);
  const reference = useRef(newRedemptionReference());
  const action = useAction();

  async function redeem() {
    if (code.trim() === "") return setProblem(t("code.required"));
    const cents = parseRandToCents(amount);
    if (cents === null || cents <= 0) return setProblem(t("redeem.invalid"));
    setProblem("");
    setDone(null);
    const r = await action.run(() => redeemVoucher(ctx.api, caller, { code, amountCents: cents, reference: reference.current }));
    if (r.ok) {
      setDone(r.value);
      setCode("");
      setAmount("");
      reference.current = newRedemptionReference();
    }
  }

  return (
    <Stack>
      <TextField label={t("redeem.code")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" />
      <TextField label={t("redeem.amount")} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} error={problem || null} />
      <ErrorNotice error={action.error} t={kit} />
      <Inline><Button onClick={() => void redeem()} disabled={action.busy}>{t("redeem.button")}</Button></Inline>
      {done ? (
        <Notice kind="success">
          <Stack>
            <span>{t("redeem.done", { amount: formatRand(done.amountCents), balance: formatRand(done.balanceCents) })}</span>
            <span>{t("redeem.reference", { reference: done.reference })}</span>
            <span>{t("redeem.referenceNote")}</span>
          </Stack>
        </Notice>
      ) : null}
    </Stack>
  );
}
