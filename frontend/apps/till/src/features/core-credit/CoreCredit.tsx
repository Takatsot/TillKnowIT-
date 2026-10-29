import { Button, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { creditPaymentRecord, formatRand, isSaleEntry, newId, parseRandToCents, queueBuilt, type CreditEntryDto } from "@tillknowit/domain";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useBusy, useT } from "../../coreKit";
import { cachedCreditAccounts, loadCreditAccounts, type CreditAccount } from "../../creditAccounts";
import type { TillContext } from "../../feature";
import { messages } from "./messages";

type Method = "cash" | "card" | "qr";

/**
 * The credit book: a customer pays off what they owe. The payment is saved on the till and sent like a sale, so it works with no connection. The balances are the
 * server's as of the last time the till was online, plus the credit entries still waiting in the queue (sales on credit, payments, cancellations).
 */
export function CoreCredit({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  const [accounts, setAccounts] = useState<CreditAccount[] | null>(null);
  const [cached, setCached] = useState(false);
  const [filter, setFilter] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<Method>("cash");
  const [message, setMessage] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const { busy, run } = useBusy();

  const load = useCallback(async () => {
    let list: CreditAccount[];
    if (ctx.online) {
      const result = await loadCreditAccounts(ctx.api, ctx.device, ctx.kv);
      list = result.accounts;
      setCached(result.cached);
    } else {
      list = await cachedCreditAccounts(ctx.kv);
      setCached(true);
    }
    // What is still in the queue has not reached the server's balance yet.
    const waiting = new Map<string, number>();
    for (const e of await ctx.outbox.all()) {
      if (isSaleEntry(e) || e.kind !== "credit_entry" || e.state !== "pending") continue;
      const entry = e.record as CreditEntryDto;
      waiting.set(entry.accountId, (waiting.get(entry.accountId) ?? 0) + entry.amountCents);
    }
    setAccounts(list.map((a) => ({ ...a, balanceCents: a.balanceCents + (waiting.get(a.id) ?? 0) })));
  }, [ctx.api, ctx.device, ctx.kv, ctx.online, ctx.outbox]);

  useEffect(() => {
    void load();
  }, [load]);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return (accounts ?? []).filter((a) => q === "" || a.name.toLowerCase().includes(q) || (a.phone ?? "").replace(/\s/g, "").includes(q.replace(/\s/g, "")));
  }, [accounts, filter]);

  const account = accounts?.find((a) => a.id === selected) ?? null;

  async function submit() {
    if (!account) return;
    const cents = parseRandToCents(amount);
    if (cents === null || cents <= 0) return setMessage({ kind: "danger", text: t("invalid") });
    if (cents > account.balanceCents) return setMessage({ kind: "danger", text: t("tooMuch", { name: account.name, amount: formatRand(Math.max(0, account.balanceCents)) }) });
    const now = new Date();
    const built = creditPaymentRecord({ accountId: account.id, amountCents: cents, paymentMethodCode: method }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now });
    if (!built.ok) return setMessage({ kind: "danger", text: t("saveFailed", { message: built.message }) });
    await queueBuilt(ctx.outbox, "credit_entry", built, now);
    ctx.nudgeSync();
    setMessage({ kind: "success", text: t("saved", { amount: formatRand(cents), name: account.name, left: formatRand(account.balanceCents - cents) }) });
    setSelected(null);
    setAmount("");
    await load();
  }

  if (account) {
    return (
      <Stack>
        <Button variant="quiet" onClick={() => { setSelected(null); setMessage(null); }}>{t("back")}</Button>
        <h2>{account.name}</h2>
        <p>{account.balanceCents > 0 ? t("owes", { name: account.name, amount: formatRand(account.balanceCents) }) : t("paysNothing", { name: account.name })}</p>
        <TextField label={t("amount")} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        <SelectField label={t("method")} value={method} onChange={(e) => setMethod(e.target.value as Method)}>
          {(["cash", "card", "qr"] as const).map((m) => <option key={m} value={m}>{t(`method.${m}`)}</option>)}
        </SelectField>
        {message && <Notice kind={message.kind}>{message.text}</Notice>}
        <Button disabled={busy || amount.trim() === "" || account.balanceCents <= 0} onClick={() => void run(submit)}>{t("save")}</Button>
      </Stack>
    );
  }

  return (
    <Stack>
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      {accounts === null ? null : accounts.length === 0 ? <p>{t("empty")}</p> : (
        <>
          {cached && <p>{ctx.online ? t("unreachable") : t("offlineCopy")}</p>}
          <TextField label={t("find")} value={filter} onChange={(e) => setFilter(e.target.value)} />
          <Table
            rows={shown} rowKey={(a) => a.id} empty={t("noMatch")}
            columns={[
              { header: t("col.name"), cell: (a) => a.name },
              { header: t("col.phone"), cell: (a) => a.phone ?? "" },
              { header: t("col.owes"), align: "right", cell: (a) => formatRand(a.balanceCents) },
              { header: t("col.limit"), align: "right", cell: (a) => (a.creditLimitCents === null ? "" : formatRand(a.creditLimitCents)) },
              { header: t("col.choose"), cell: (a) => <Button variant="secondary" aria-label={t("choose", { name: a.name })} disabled={a.balanceCents <= 0} onClick={() => { setSelected(a.id); setAmount((a.balanceCents / 100).toFixed(2)); setMessage(null); }}>{t("col.choose")}</Button> },
            ]}
          />
        </>
      )}
    </Stack>
  );
}
