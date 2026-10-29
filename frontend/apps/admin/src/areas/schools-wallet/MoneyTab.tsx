import { formatDate, formatRand, parseRandToCents, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import type { School, StaffMonth } from "./types";

export function MoneyTab({ school }: { school: School }) {
  return (
    <Stack>
      <Deposit school={school} />
      <Staff school={school} />
    </Stack>
  );
}

function Deposit({ school }: { school: School }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [amount, setAmount] = useState("");
  const [reference, setReference] = useState("");
  const [bad, setBad] = useState<string | null>(null);
  const [done, setDone] = useState("");
  const post = useAction((cents: number) => api.post<void>(`/admin/schools/${school.id}/cash-deposits`, undefined, { callerPersonId: personId, amountCents: cents, reference: reference.trim() }));

  async function submit() {
    setDone("");
    const value = parseRandToCents(amount);
    setBad(value === null || value === 0 ? t("amountInvalid") : null);
    if (value === null || value === 0) return;
    const r = await post.run(value);
    if (r.ok) {
      setDone(t("depositDone", { amount: formatRand(value) }));
      setAmount("");
      setReference("");
    }
  }

  return (
    <Card title={t("depositHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("depositIntro")}</p>
          <Inline>
            <TextField label={t("amount")} inputMode="decimal" value={amount} error={bad} onChange={(e) => setAmount(e.target.value)} required />
            <TextField label={t("reference")} value={reference} onChange={(e) => setReference(e.target.value)} required />
            <Button type="submit" disabled={post.busy || reference.trim() === ""}>{t("depositRun")}</Button>
          </Inline>
          {post.error && <Notice kind="danger">{post.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

function Staff({ school }: { school: School }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [month, setMonth] = useState(() => formatDate(new Date()).slice(0, 7));
  const [statement, setStatement] = useState<StaffMonth | null>(null);
  const [done, setDone] = useState("");
  const show = useAction(() => api.get<StaffMonth>(`/schools/${school.id}/staff-accounts/${month.trim()}`, { callerPersonId: personId }));
  const settle = useAction(() => api.post<StaffMonth>(`/admin/schools/${school.id}/staff-accounts/${month.trim()}/settle`, undefined, { callerPersonId: personId }));

  async function look() {
    setDone("");
    setStatement(null);
    const r = await show.run();
    if (r.ok) setStatement(r.value);
  }

  async function run() {
    const r = await settle.run();
    if (r.ok) {
      setStatement(r.value);
      setDone(t("settled", { month: r.value.month }));
    }
  }

  return (
    <Card title={t("staffHeading")}>
      <Stack>
        <p>{t("staffIntro")}</p>
        <form onSubmit={(e) => { e.preventDefault(); void look(); }}>
          <Inline>
            <TextField label={t("month")} hint={t("monthHint")} value={month} onChange={(e) => { setMonth(e.target.value); setStatement(null); setDone(""); }} required />
            <Button type="submit" variant="secondary" disabled={show.busy || month.trim() === ""}>{t("showMonth")}</Button>
          </Inline>
        </form>
        {show.error && <Notice kind="danger">{show.error.message}</Notice>}
        {statement && (
          <>
            <p>{t("staffTotal", { month: statement.month, total: formatRand(statement.totalOwedCents) })}</p>
            <Table
              rows={statement.teachers}
              rowKey={(x) => x.holderId}
              empty={t("staffEmpty")}
              columns={[
                { header: t("colTeacher"), cell: (x) => x.name },
                { header: t("colOwed"), cell: (x) => formatRand(x.owedCents), align: "right" },
              ]}
            />
            <Button disabled={settle.busy} onClick={() => void run()}>{t("settle", { month: statement.month })}</Button>
          </>
        )}
        {settle.error && <Notice kind="danger">{settle.error.message}</Notice>}
        {done && <Notice kind="success">{done}</Notice>}
      </Stack>
    </Card>
  );
}
