import { newId } from "@tillknowit/domain";
import { formatDate, formatRand, parseRandToCents, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import type { TillCaller } from "../selling-common/TillCaller";
import { messages } from "./messages";

interface Issued { id: string; code: string; balanceCents: number; expiresOn: string | null }
interface Voucher { id: string; codeLast4: string; balanceCents: number; expiresOn: string | null; isVoid: boolean; isExpired: boolean }

export function Vouchers({ caller }: { caller: TillCaller }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const till = { deviceId: caller.deviceId, staffId: caller.staffId };

  const [amount, setAmount] = useState("");
  const [paidWith, setPaidWith] = useState("cash");
  const [amountProblem, setAmountProblem] = useState<string | null>(null);
  const [issued, setIssued] = useState<Issued | null>(null);
  const issue = useAction((cents: number) => api.post<Issued>(`/shops/${shopId}/vouchers`, { ...till, amountCents: cents, paidWith, reference: newId(), saleId: null }));

  const [code, setCode] = useState("");
  const [found, setFound] = useState<Voucher | null>(null);
  const check = useAction(() => api.post<Voucher>(`/shops/${shopId}/vouchers/check`, { code: code.trim(), ...till }));

  const [useAmount, setUseAmount] = useState("");
  const [useProblem, setUseProblem] = useState<string | null>(null);
  const [used, setUsed] = useState("");
  const redeem = useAction((cents: number) => api.post<Voucher>(`/shops/${shopId}/vouchers/redeem`, { ...till, code: code.trim(), amountCents: cents, reference: newId(), saleId: null }));

  async function doIssue() {
    const cents = parseRandToCents(amount);
    if (cents === null || cents === 0) { setAmountProblem(t("amountInvalid")); return; }
    setAmountProblem(null);
    setIssued(null);
    const r = await issue.run(cents);
    if (r.ok) { setIssued(r.value); setAmount(""); }
  }

  async function doCheck() {
    setUsed("");
    const r = await check.run();
    setFound(r.ok ? r.value : null);
  }

  async function doRedeem() {
    const cents = parseRandToCents(useAmount);
    if (cents === null || cents === 0) { setUseProblem(t("amountInvalid")); return; }
    setUseProblem(null);
    const r = await redeem.run(cents);
    if (r.ok) { setFound(r.value); setUsed(t("voucherRedeemed", { balance: formatRand(r.value.balanceCents) })); setUseAmount(""); }
  }

  return (
    <Stack>
      <Card title={t("voucherIssueHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); void doIssue(); }}>
          <Stack>
            <Inline>
              <TextField label={t("voucherAmount")} inputMode="decimal" value={amount} error={amountProblem} onChange={(e) => setAmount(e.target.value)} required />
              <SelectField label={t("voucherPaidWith")} value={paidWith} onChange={(e) => setPaidWith(e.target.value)}>
                <option value="cash">{t("cash")}</option>
                <option value="card">{t("card")}</option>
                <option value="qr">{t("qr")}</option>
              </SelectField>
              <Button type="submit" disabled={issue.busy || !caller.ready}>{t("voucherIssue")}</Button>
            </Inline>
            {issue.error && <Notice kind="danger">{issue.error.message}</Notice>}
            {issued && (
              <Notice kind="warning">
                <strong>{t("voucherCode", { code: issued.code })}</strong>{" "}
                {t("voucherBalance", { balance: formatRand(issued.balanceCents) })} {issued.expiresOn ? t("voucherExpires", { date: formatDate(issued.expiresOn) }) : ""}
                <br />{t("voucherCodeNote")}
              </Notice>
            )}
          </Stack>
        </form>
      </Card>
      <Card title={t("voucherCheckHeading")}>
        <Stack>
          <form onSubmit={(e) => { e.preventDefault(); void doCheck(); }}>
            <Inline>
              <TextField label={t("voucherCodeField")} autoComplete="off" value={code} onChange={(e) => { setCode(e.target.value); setFound(null); }} required />
              <Button type="submit" disabled={check.busy || !caller.ready || code.trim() === ""}>{t("voucherCheck")}</Button>
            </Inline>
          </form>
          {check.error && <Notice kind="danger">{check.error.message}</Notice>}
          {found && (
            <Notice kind={found.isVoid || found.isExpired ? "warning" : "info"}>
              {t("voucherFound", { last4: found.codeLast4, balance: formatRand(found.balanceCents) })}
              {found.isVoid ? ` ${t("voucherVoid")}` : ""}{found.isExpired ? ` ${t("voucherExpired")}` : ""}
            </Notice>
          )}
        </Stack>
      </Card>
      <Card title={t("voucherRedeemHeading")}>
        <Stack>
          <form onSubmit={(e) => { e.preventDefault(); void doRedeem(); }}>
            <Inline>
              <TextField label={t("voucherRedeemAmount")} inputMode="decimal" value={useAmount} error={useProblem} onChange={(e) => setUseAmount(e.target.value)} required />
              <Button type="submit" disabled={redeem.busy || !caller.ready || code.trim() === ""}>{t("voucherRedeem")}</Button>
            </Inline>
          </form>
          {redeem.error && <Notice kind="danger">{redeem.error.message}</Notice>}
          {used && <Notice kind="success">{used}</Notice>}
        </Stack>
      </Card>
    </Stack>
  );
}
