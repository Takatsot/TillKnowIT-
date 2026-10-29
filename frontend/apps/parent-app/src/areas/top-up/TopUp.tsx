import { formatRand, parseRandToCents, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useRef, useState } from "react";
import { bigStyle, ChildScope, itemStyle, listStyle, mutedStyle, wrapStyle, type ChildContext } from "../../shared/ChildScope";
import type { TopUp as TopUpResponse } from "../../shared/types";
import { messages } from "./messages";

interface Started {
  topUp: TopUpResponse;
  name: string;
  balanceBefore: number;
}

export function TopUp() {
  return <ChildScope render={(ctx) => <Body ctx={ctx} />} />;
}

function Body({ ctx }: { ctx: ChildContext }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const { child } = ctx;
  const [amount, setAmount] = useState("");
  const [started, setStarted] = useState<Started[]>([]);
  const cents = parseRandToCents(amount);

  const start = useAction((amountCents: number) => api.post<TopUpResponse>(`/wallets/${child.walletId}/top-ups`, { callerPersonId: personId, amountCents, returnUrl: window.location.href }));

  async function submit() {
    if (cents === null || cents <= 0) return;
    const r = await start.run(cents);
    if (r.ok) {
      setStarted((list) => [{ topUp: r.value, name: child.name, balanceBefore: child.balanceCents }, ...list]);
      setAmount("");
    }
  }

  /** The server tells us "pending" when the top-up starts and has no way to ask again; the balance going up is how we see the money arrive. */
  function stateOf(s: Started): "completed" | "failed" | "pending" {
    if (s.topUp.status === "completed" || s.topUp.status === "failed") return s.topUp.status;
    const now = ctx.children.find((c) => c.walletId === s.topUp.walletId)?.balanceCents ?? s.balanceBefore;
    return now >= s.balanceBefore + s.topUp.amountCents ? "completed" : "pending";
  }

  return (
    <Stack>
      <Card title={t("addHeading")}>
        <span style={mutedStyle}>{t("balance")}</span>
        <p style={bigStyle}>{formatRand(child.balanceCents)}</p>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <TextField
              label={t("amount")} hint={t("amountHint")} value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal"
              error={amount !== "" && (cents === null || cents <= 0) ? t("amountBad") : null}
            />
            {start.error && <Notice kind="danger">{start.error.message}</Notice>}
            <Button type="submit" disabled={start.busy || cents === null || cents <= 0}>{t("start")}</Button>
          </Stack>
        </form>
      </Card>

      {started.length > 0 && (
        <Card title={t("recent")}>
          <ul style={listStyle}>
            {started.map((s) => {
              const state = stateOf(s);
              return (
                <li key={s.topUp.id} style={itemStyle}>
                  <strong>{t("topUpLine", { amount: formatRand(s.topUp.amountCents), name: s.name })}</strong>
                  <span><Badge>{state === "completed" ? t("confirmed") : state === "failed" ? t("failed") : t("pending")}</Badge></span>
                  {state === "pending" && (
                    <>
                      <p style={mutedStyle}>{t("pendingHelp")}</p>
                      {s.topUp.checkoutUrl && (
                        <>
                          <a href={s.topUp.checkoutUrl} target="_blank" rel="noopener noreferrer" style={wrapStyle}>{t("pay")}</a>
                          {s.topUp.checkoutUrl.includes(".invalid") && <Notice kind="warning">{t("devNote")}</Notice>}
                        </>
                      )}
                      <Button variant="secondary" onClick={ctx.reload}>{t("check")}</Button>
                    </>
                  )}
                </li>
              );
            })}
          </ul>
        </Card>
      )}

      <Transfer ctx={ctx} />
    </Stack>
  );
}

function Transfer({ ctx }: { ctx: ChildContext }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const { child } = ctx;
  const others = ctx.children.filter((c) => c.walletId !== child.walletId);
  const [toId, setToId] = useState("");
  const [amount, setAmount] = useState("");
  const [done, setDone] = useState("");
  // One reference per attempt: if the answer is lost and the parent presses again, the server sees the same request and does it once.
  const reference = useRef(crypto.randomUUID());
  const to = others.find((c) => c.walletId === toId) ?? others[0];
  const cents = parseRandToCents(amount);

  const move = useAction((amountCents: number, target: string) =>
    api.post("/wallets/transfers", { callerPersonId: personId, fromWalletId: child.walletId, toWalletId: target, amountCents, reference: reference.current }));

  if (!to) return <Notice>{t("needTwo")}</Notice>;

  async function submit() {
    if (!to || cents === null || cents <= 0) return;
    setDone("");
    const r = await move.run(cents, to.walletId);
    if (r.ok) {
      setDone(t("transferred", { amount: formatRand(cents), from: child.name, to: to.name }));
      setAmount("");
      reference.current = crypto.randomUUID();
      ctx.reload();
    }
  }

  return (
    <Card title={t("transferHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <strong>{t("transferFrom", { name: child.name })}</strong>
          <SelectField label={t("to")} value={to.walletId} onChange={(e) => setToId(e.target.value)}>
            {others.map((c) => <option key={c.walletId} value={c.walletId}>{c.name}</option>)}
          </SelectField>
          <TextField label={t("transferAmount")} value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" />
          {move.error && <Notice kind="danger">{move.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
          <Button type="submit" disabled={move.busy || cents === null || cents <= 0}>{t("transfer")}</Button>
        </Stack>
      </form>
    </Card>
  );
}
