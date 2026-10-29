import { Button, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { businessDayOf, cashDifference, cashUpRecord, formatRand, newId, parseRandToCents, queueBuilt } from "@tillknowit/domain";
import { useEffect, useState } from "react";
import { useBusy, useT, type Translate } from "../../coreKit";
import type { TillContext } from "../../feature";
import { cashUpFigures, lastCashUpDay, setLastCashUpDay, type CashUpFigures } from "../../ledger";
import { messages, type CashUpKey } from "./messages";

const FLOAT_KEY = "cashup.float";

function differenceText(t: Translate<CashUpKey>, difference: number): string {
  return difference < 0 ? t("short", { amount: formatRand(-difference) }) : difference > 0 ? t("over", { amount: formatRand(difference) }) : t("balanced");
}

/**
 * End of day: the float, what the till expects in the drawer (worked out from this till's own sales, credit payments and refunds with the domain's cash-up rules),
 * what was counted, and the difference. The cash-up is queued and sent like a sale; the server works the expected cash out again from what really synced.
 */
export function CoreCashUp({ ctx }: { ctx: TillContext }) {
  const t = useT(messages, ctx.language);
  const day = businessDayOf(new Date());
  const [float, setFloat] = useState("");
  const [counted, setCounted] = useState("");
  const [note, setNote] = useState("");
  const [figures, setFigures] = useState<CashUpFigures | null>(null);
  const [already, setAlready] = useState(false);
  const [message, setMessage] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const { busy, run } = useBusy();

  const floatCents = float.trim() === "" ? 0 : parseRandToCents(float);

  // The float last used is the likely one today too.
  useEffect(() => {
    void (async () => {
      const last = await ctx.kv?.get<number>(FLOAT_KEY);
      if (last !== undefined) setFloat((last / 100).toFixed(2));
      setAlready((await (ctx.kv ? lastCashUpDay(ctx.kv) : undefined)) === day);
    })();
  }, [ctx.kv, day]);

  useEffect(() => {
    if (!ctx.kv || floatCents === null) return setFigures(null);
    let live = true;
    void cashUpFigures(ctx.kv, day, floatCents, new Date()).then((f) => live && setFigures(f));
    return () => { live = false; };
  }, [ctx.kv, day, floatCents]);

  const countedCents = counted.trim() === "" ? null : parseRandToCents(counted);
  const difference = figures && countedCents !== null ? cashDifference(countedCents, figures.expectedCents) : null;

  async function submit() {
    if (!ctx.kv || floatCents === null || countedCents === null) return setMessage({ kind: "danger", text: t("invalid") });
    const now = new Date();
    const fresh = await cashUpFigures(ctx.kv, businessDayOf(now), floatCents, now);
    const built = cashUpRecord({ floatCents, expectedCents: fresh.expectedCents, countedCents, note }, { id: newId(now.getTime()), staffId: ctx.staff.id, at: now });
    if (!built.ok) return setMessage({ kind: "danger", text: t("saveFailed", { message: built.message }) });
    await queueBuilt(ctx.outbox, "cash_up", built, now);
    await ctx.kv.set(FLOAT_KEY, floatCents);
    await setLastCashUpDay(ctx.kv, built.value.day);
    ctx.nudgeSync();
    setAlready(true);
    setCounted("");
    setNote("");
    setMessage({ kind: "success", text: t("saved", { day: built.value.day, result: differenceText(t, cashDifference(countedCents, fresh.expectedCents)) }) });
  }

  return (
    <Stack>
      <p>{t("intro", { day })}</p>
      {already && <Notice kind="warning">{t("already", { day })}</Notice>}
      <TextField label={t("float")} inputMode="decimal" value={float} onChange={(e) => setFloat(e.target.value)} error={floatCents === null ? t("invalid") : null} />
      {figures && (
        <Table
          rows={[
            { label: t("row.float"), cents: figures.floatCents },
            { label: t("row.sales"), cents: figures.cashSalesCents },
            { label: t("row.credit"), cents: figures.creditCashCents },
            { label: t("row.refunds"), cents: -figures.cashRefundsCents },
            { label: t("row.expected"), cents: figures.expectedCents },
          ]}
          rowKey={(r) => r.label}
          columns={[{ header: t("col.what"), cell: (r) => r.label }, { header: t("col.amount"), align: "right", cell: (r) => formatRand(r.cents) }]}
        />
      )}
      <TextField label={t("counted")} inputMode="decimal" value={counted} onChange={(e) => setCounted(e.target.value)} error={counted.trim() !== "" && countedCents === null ? t("invalid") : null} />
      {difference !== null && <Notice kind={difference === 0 ? "success" : "warning"}>{differenceText(t, difference)}</Notice>}
      <TextField label={t("note")} value={note} onChange={(e) => setNote(e.target.value)} />
      {message && <Notice kind={message.kind}>{message.text}</Notice>}
      <Button disabled={busy || countedCents === null || floatCents === null} onClick={() => void run(submit)}>{t("save")}</Button>
    </Stack>
  );
}
