import { parseRandToCents, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type SchoolsKey } from "./messages";
import type { School } from "./types";

/** Basis points shown as a percentage: 250 is "2.5". */
const bpsToPercent = (bps: number) => String(bps / 100);

export function SchoolTab({ school, onSchool }: { school: School | null; onSchool: (s: School) => void }) {
  return (
    <Stack>
      <Register onSchool={onSchool} />
      {school && (
        <>
          <Details school={school} onSchool={onSchool} />
          <Settings key={school.id} school={school} onSchool={onSchool} />
          <Admins school={school} />
        </>
      )}
    </Stack>
  );
}

function Register({ onSchool }: { onSchool: (s: School) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [name, setName] = useState("");
  const [emis, setEmis] = useState("");
  const [shopId, setShopId] = useState("");
  const [done, setDone] = useState("");
  const create = useAction(() => api.post<School>("/admin/schools", { callerPersonId: personId, name: name.trim(), emisNumber: emis.trim() === "" ? null : emis.trim(), shopId: shopId.trim() }));

  async function submit() {
    setDone("");
    const r = await create.run();
    if (r.ok) {
      setDone(t("registered", { name: r.value.name }));
      setName("");
      setEmis("");
      setShopId("");
      onSchool(r.value);
    }
  }

  return (
    <Card title={t("registerHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("schoolName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("emis")} value={emis} onChange={(e) => setEmis(e.target.value)} />
            <TextField label={t("shopId")} hint={t("shopHint")} value={shopId} onChange={(e) => setShopId(e.target.value)} required />
          </Inline>
          {create.error && <Notice kind="danger">{create.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
          <Button type="submit" disabled={create.busy || name.trim() === "" || shopId.trim() === ""}>{t("register")}</Button>
        </Stack>
      </form>
    </Card>
  );
}

function Details({ school, onSchool }: { school: School; onSchool: (s: School) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [done, setDone] = useState("");
  const verify = useAction(() => api.post<School>(`/admin/schools/${school.id}/verify`, undefined, { callerPersonId: personId }));

  async function run() {
    const r = await verify.run();
    if (r.ok) {
      setDone(t("schoolVerified", { name: r.value.name }));
      onSchool(r.value);
    }
  }

  return (
    <Card title={t("detailsHeading", { name: school.name })}>
      <Stack>
        <Inline>
          <span>{t("status")}:</span>
          <Badge>{t(school.status as SchoolsKey)}</Badge>
        </Inline>
        <p>{t("fee", { fee: `${bpsToPercent(school.feeBasisPoints)}%` })}</p>
        <p>{t("schedule", { schedule: t(school.payoutSchedule as SchoolsKey) })}</p>
        {school.status !== "verified" && <Button disabled={verify.busy} onClick={() => void run()}>{t("verify")}</Button>}
        {verify.error && <Notice kind="danger">{verify.error.message}</Notice>}
        {done && <Notice kind="success">{done}</Notice>}
      </Stack>
    </Card>
  );
}

function Settings({ school, onSchool }: { school: School; onSchool: (s: School) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [fee, setFee] = useState(bpsToPercent(school.feeBasisPoints));
  const [schedule, setSchedule] = useState(school.payoutSchedule);
  const [staff, setStaff] = useState(school.staffAccountsEnabled);
  const [ceiling, setCeiling] = useState(school.dailyCeilingCents === null ? "" : String(school.dailyCeilingCents / 100));
  const [split, setSplit] = useState(String(school.offlineSplitMinutes));
  const [bad, setBad] = useState<{ fee?: string; ceiling?: string; split?: string }>({});
  const [done, setDone] = useState(false);
  const save = useAction((body: unknown) => api.put<School>(`/schools/${school.id}/settings`, body));

  async function submit() {
    setDone(false);
    const problems: typeof bad = {};
    const feeNumber = Number(fee.replace(",", "."));
    if (fee.trim() === "" || !Number.isFinite(feeNumber) || feeNumber < 0) problems.fee = t("feeInvalid");
    const ceilingCents = ceiling.trim() === "" ? null : parseRandToCents(ceiling);
    if (ceiling.trim() !== "" && ceilingCents === null) problems.ceiling = t("ceilingInvalid");
    if (!/^\d+$/.test(split.trim())) problems.split = t("splitInvalid");
    setBad(problems);
    if (problems.fee || problems.ceiling || problems.split) return;
    const r = await save.run({
      callerPersonId: personId, feeBasisPoints: Math.round(feeNumber * 100), payoutSchedule: schedule, staffAccountsEnabled: staff,
      dailyCeilingCents: ceilingCents, offlineSplitMinutes: Number(split.trim()),
    });
    if (r.ok) {
      setDone(true);
      onSchool(r.value);
    }
  }

  return (
    <Card title={t("settingsHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("feePercent")} hint={t("feeHint")} inputMode="decimal" value={fee} error={bad.fee ?? null} onChange={(e) => setFee(e.target.value)} />
            <SelectField label={t("payoutSchedule")} value={schedule} onChange={(e) => setSchedule(e.target.value)}>
              <option value="daily">{t("daily")}</option>
              <option value="weekly">{t("weekly")}</option>
              <option value="monthly">{t("monthly")}</option>
            </SelectField>
          </Inline>
          <Inline>
            <TextField label={t("ceiling")} hint={t("ceilingHint")} inputMode="decimal" value={ceiling} error={bad.ceiling ?? null} onChange={(e) => setCeiling(e.target.value)} />
            <TextField label={t("split")} hint={t("splitHint")} inputMode="numeric" value={split} error={bad.split ?? null} onChange={(e) => setSplit(e.target.value)} />
          </Inline>
          <CheckField label={t("staffAccounts")} checked={staff} onChange={(e) => setStaff(e.target.checked)} />
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          {done && <Notice kind="success">{t("settingsSaved")}</Notice>}
          <Button type="submit" disabled={save.busy}>{t("saveSettings")}</Button>
        </Stack>
      </form>
    </Card>
  );
}

function Admins({ school }: { school: School }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [person, setPerson] = useState("");
  const [done, setDone] = useState(false);
  const add = useAction(() => api.post<void>(`/schools/${school.id}/admins`, { callerPersonId: personId, personId: person.trim() }));

  async function submit() {
    setDone(false);
    const r = await add.run();
    if (r.ok) {
      setDone(true);
      setPerson("");
    }
  }

  return (
    <Card title={t("adminsHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("adminsHint")}</p>
          <Inline>
            <TextField label={t("personId")} value={person} onChange={(e) => setPerson(e.target.value)} required />
            <Button type="submit" disabled={add.busy || person.trim() === ""}>{t("addAdmin")}</Button>
          </Inline>
          {add.error && <Notice kind="danger">{add.error.message}</Notice>}
          {done && <Notice kind="success">{t("adminAdded")}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}
