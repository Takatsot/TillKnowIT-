import { newId } from "@tillknowit/domain";
import { formatDate, formatDateTime, formatRand, parseRandToCents, saveFile, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { useTillCaller, type TillCaller } from "../selling-common/TillCaller";
import { messages, type ServicesKey } from "./messages";

interface Service { id: string; name: string; durationMinutes: number; priceCents: number; isActive: boolean; staffIds: string[] }
interface Appointment {
  id: string; serviceId: string; serviceName: string; staffId: string; customerId: string | null; customerName: string; customerPhone: string | null;
  startsAt: string; endsAt: string; status: string; hasConflict: boolean; priceCents: number; notes: string | null;
}
interface Rule { id: string; staffId: string | null; serviceId: string | null; percent: number }

type Tab = "services" | "calendar" | "commission";

const none = <T,>(): Promise<{ ok: true; value: T[] }> => Promise.resolve({ ok: true as const, value: [] });

export function ServicesAppointments() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const caller = useTillCaller();
  const [tab, setTab] = useState<Tab>("services");
  const services = useLoad(
    () => (caller.ready ? api.get<Service[]>(`/shops/${shopId}/services`, { deviceId: caller.deviceId, staffId: caller.staffId }) : none<Service>()),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const tabs: [Tab, string][] = [["services", t("tabServices")], ["calendar", t("tabCalendar")], ["commission", t("tabCommission")]];
  return (
    <Stack>
      {caller.card}
      <Inline>
        {tabs.map(([id, label]) => <Button key={id} variant={tab === id ? "primary" : "secondary"} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}
      </Inline>
      {services.error && <Notice kind="danger">{services.error.message}</Notice>}
      {tab === "services" && <Services services={services.data} loading={services.loading} reload={services.reload} />}
      {tab === "calendar" && <Calendar caller={caller} services={services.data ?? []} />}
      {tab === "commission" && <Commission services={services.data ?? []} />}
    </Stack>
  );
}

// ------------------------------------------------------------------ services

function Services({ services, loading, reload }: { services: Service[] | null; loading: boolean; reload: () => void }) {
  const t = useMessages(messages);
  const [editing, setEditing] = useState<Service | null>(null);
  const [note, setNote] = useState("");
  return (
    <Stack>
      <Card title={t("servicesHeading")}>
        <Stack>
          {note && <Notice kind="success">{note}</Notice>}
          {loading && !services ? <p>{t("loading")}</p> : (
            <Table
              rows={services ?? []}
              rowKey={(s) => s.id}
              empty={t("servicesEmpty")}
              columns={[
                { header: t("colService"), cell: (s) => s.name },
                { header: t("colDuration"), cell: (s) => s.durationMinutes, align: "right" },
                { header: t("colPrice"), cell: (s) => formatRand(s.priceCents), align: "right" },
                { header: t("colStaff"), cell: (s) => (s.staffIds.length === 0 ? t("anyStaff") : s.staffIds.join(", ")) },
                { header: t("colActive"), cell: (s) => <Badge>{s.isActive ? t("yes") : t("no")}</Badge> },
                { header: t("colActions"), cell: (s) => <Button variant="secondary" onClick={() => { setNote(""); setEditing(s); }}>{t("editService", { name: s.name })}</Button> },
              ]}
            />
          )}
        </Stack>
      </Card>
      <ServiceForm key={editing?.id ?? "new"} editing={editing} onSaved={(s) => { setNote(t("serviceSaved", { name: s.name })); setEditing(null); reload(); }} onCancel={() => setEditing(null)} />
    </Stack>
  );
}

function ServiceForm({ editing, onSaved, onCancel }: { editing: Service | null; onSaved: (s: Service) => void; onCancel: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const [name, setName] = useState(editing?.name ?? "");
  const [minutes, setMinutes] = useState(String(editing?.durationMinutes ?? 30));
  const [price, setPrice] = useState(editing ? (editing.priceCents / 100).toFixed(2) : "");
  const [staff, setStaff] = useState(editing?.staffIds.join(", ") ?? "");
  const [active, setActive] = useState(editing?.isActive ?? true);
  const [problem, setProblem] = useState<string | null>(null);
  const save = useAction((body: unknown) => (editing ? api.put<Service>(`/shops/${shopId}/services/${editing.id}`, body) : api.post<Service>(`/shops/${shopId}/services`, body)));

  async function submit() {
    const priceCents = parseRandToCents(price);
    if (priceCents === null) { setProblem(t("moneyInvalid")); return; }
    setProblem(null);
    const staffIds = staff.split(",").map((x) => x.trim()).filter(Boolean);
    const r = await save.run({ callerPersonId: personId, name: name.trim(), durationMinutes: Number(minutes), priceCents, isActive: active, staffIds });
    if (r.ok) { if (!editing) { setName(""); setPrice(""); setStaff(""); } onSaved(r.value); }
  }

  return (
    <Card title={editing ? t("serviceEdit") : t("serviceNew")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("serviceName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("serviceMinutes")} type="number" min={5} value={minutes} onChange={(e) => setMinutes(e.target.value)} required />
            <TextField label={t("servicePrice")} inputMode="decimal" value={price} error={problem} onChange={(e) => setPrice(e.target.value)} required />
          </Inline>
          <TextField label={t("serviceStaff")} hint={t("serviceStaffHint")} value={staff} onChange={(e) => setStaff(e.target.value)} />
          <CheckField label={t("serviceActive")} checked={active} onChange={(e) => setActive(e.target.checked)} />
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          <Inline>
            <Button type="submit" disabled={save.busy || name.trim() === ""}>{t("saveService")}</Button>
            {editing && <Button variant="quiet" onClick={onCancel}>{t("stopEditing")}</Button>}
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}

// ------------------------------------------------------------------ calendar

const nextDay = (day: string) => new Date(Date.parse(`${day}T00:00:00Z`) + 86_400_000).toISOString().slice(0, 10);
const statusKey = (status: string): ServicesKey => `status_${status.replace("_", "")}` as ServicesKey;
const open = (status: string) => status === "booked" || status === "confirmed";

function Calendar({ caller, services }: { caller: TillCaller; services: readonly Service[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [day, setDay] = useState(formatDate(new Date()));
  const [note, setNote] = useState("");
  const [moving, setMoving] = useState<{ id: string; time: string } | null>(null);
  const list = useLoad(
    () => (caller.ready && day !== ""
      ? api.get<Appointment[]>(`/shops/${shopId}/appointments`, { deviceId: caller.deviceId, staffId: caller.staffId, from: `${day}T00:00:00+02:00`, to: `${nextDay(day)}T00:00:00+02:00` })
      : none<Appointment>()),
    [shopId, caller.deviceId, caller.staffId, caller.ready, day],
  );
  const act = useAction((a: Appointment, action: string) => api.post<Appointment>(`/shops/${shopId}/appointments/${a.id}/action`, { deviceId: caller.deviceId, staffId: caller.staffId, action }));
  const move = useAction((a: Appointment, time: string) => api.patch<Appointment>(`/shops/${shopId}/appointments/${a.id}`, { deviceId: caller.deviceId, staffId: caller.staffId, startsAt: `${day}T${time}:00+02:00`, providerStaffId: null }));

  async function run(a: Appointment, action: string) {
    setNote("");
    const r = await act.run(a, action);
    if (r.ok) { setNote(t("actionDone", { name: a.customerName, status: t(statusKey(r.value.status)) })); list.reload(); }
  }

  async function reschedule(a: Appointment) {
    if (!moving) return;
    setNote("");
    const r = await move.run(a, moving.time);
    if (r.ok) { setNote(t("rescheduled", { name: a.customerName })); setMoving(null); list.reload(); }
  }

  return (
    <Stack>
      <Card title={t("calendarHeading", { day })}>
        <Stack>
          <TextField label={t("dayLabel")} type="date" value={day} onChange={(e) => { setNote(""); setDay(e.target.value); }} />
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {act.error && <Notice kind="danger">{act.error.message}</Notice>}
          {move.error && <Notice kind="danger">{move.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(a) => a.id}
              empty={t("calendarEmpty")}
              columns={[
                { header: t("colTime"), cell: (a) => `${formatDateTime(a.startsAt).slice(11)} - ${formatDateTime(a.endsAt).slice(11)}` },
                { header: t("colService"), cell: (a) => a.serviceName },
                { header: t("colCustomer"), cell: (a) => `${a.customerName}${a.customerPhone ? ` (${a.customerPhone})` : ""}` },
                { header: t("colStatus"), cell: (a) => <><Badge>{t(statusKey(a.status))}</Badge>{a.hasConflict && <> <Badge>{t("conflict")}</Badge></>}</> },
                {
                  header: t("colActions"),
                  cell: (a) => !open(a.status) ? null : moving?.id === a.id ? (
                    <Inline>
                      <TextField label={t("rescheduleTime", { name: a.customerName })} type="time" value={moving.time} onChange={(e) => setMoving({ id: a.id, time: e.target.value })} />
                      <Button disabled={move.busy || moving.time === ""} onClick={() => void reschedule(a)}>{t("rescheduleSave", { name: a.customerName })}</Button>
                      <Button variant="quiet" onClick={() => setMoving(null)}>{t("rescheduleStop")}</Button>
                    </Inline>
                  ) : (
                    <Inline>
                      {a.status === "booked" && <Button variant="secondary" disabled={act.busy} onClick={() => void run(a, "confirm")}>{t("confirm", { name: a.customerName })}</Button>}
                      <Button variant="secondary" disabled={act.busy} onClick={() => void run(a, "complete")}>{t("complete", { name: a.customerName })}</Button>
                      <Button variant="quiet" onClick={() => setMoving({ id: a.id, time: formatDateTime(a.startsAt).slice(11) })}>{t("reschedule", { name: a.customerName })}</Button>
                      <Button variant="quiet" disabled={act.busy} onClick={() => void run(a, "no_show")}>{t("noShow", { name: a.customerName })}</Button>
                      <Button variant="quiet" disabled={act.busy} onClick={() => void run(a, "cancel")}>{t("cancel", { name: a.customerName })}</Button>
                    </Inline>
                  ),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      <BookForm caller={caller} day={day} services={services} onBooked={(msg) => { setNote(msg); list.reload(); }} />
    </Stack>
  );
}

function BookForm({ caller, day, services, onBooked }: { caller: TillCaller; day: string; services: readonly Service[]; onBooked: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [serviceId, setServiceId] = useState("");
  const [providerPick, setProvider] = useState<string | null>(null);
  const provider = providerPick ?? caller.staffId;
  const [customer, setCustomer] = useState("");
  const [phone, setPhone] = useState("");
  const [time, setTime] = useState("09:00");
  const [notes, setNotes] = useState("");
  const book = useAction((body: unknown) => api.post<Appointment>(`/shops/${shopId}/appointments`, body));

  async function submit() {
    const r = await book.run({
      deviceId: caller.deviceId, staffId: caller.staffId, id: newId(), serviceId, providerStaffId: provider.trim(), customerId: null,
      customerName: customer.trim(), customerPhone: phone.trim() === "" ? null : phone.trim(), startsAt: `${day}T${time}:00+02:00`, notes: notes.trim() === "" ? null : notes.trim(), allowConflict: false,
    });
    if (r.ok) { onBooked(t("booked", { name: customer.trim(), time: `${day} ${time}` })); setCustomer(""); setPhone(""); setNotes(""); }
  }

  return (
    <Card title={t("bookHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <SelectField label={t("bookService")} value={serviceId} onChange={(e) => setServiceId(e.target.value)} required>
              <option value="">{t("chooseService")}</option>
              {services.filter((s) => s.isActive).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </SelectField>
            <TextField label={t("bookProvider")} value={provider} onChange={(e) => setProvider(e.target.value)} required />
            <TextField label={t("bookTime")} type="time" value={time} onChange={(e) => setTime(e.target.value)} required />
          </Inline>
          <Inline>
            <TextField label={t("bookCustomerName")} value={customer} onChange={(e) => setCustomer(e.target.value)} required />
            <TextField label={t("bookCustomerPhone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
            <TextField label={t("bookNotes")} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Inline>
          {book.error && <Notice kind="danger">{book.error.message}</Notice>}
          <div><Button type="submit" disabled={book.busy || !caller.ready || serviceId === "" || customer.trim() === "" || provider.trim() === ""}>{t("bookSave")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

// ------------------------------------------------------------------ commission

function Commission({ services }: { services: readonly Service[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const rules = useLoad(() => api.get<Rule[]>(`/shops/${shopId}/commission-rules`, { callerPersonId: personId }), [shopId, personId]);
  const [staff, setStaff] = useState("");
  const [serviceId, setServiceId] = useState("");
  const [percent, setPercent] = useState("20");
  const [note, setNote] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [format, setFormat] = useState("csv");
  const [reportNote, setReportNote] = useState("");
  const save = useAction(() => api.put<Rule>(`/shops/${shopId}/commission-rules`, { callerPersonId: personId, staffId: staff.trim() === "" ? null : staff.trim(), serviceId: serviceId === "" ? null : serviceId, percent: Number(percent) }));
  const download = useAction(() => api.download(`/shops/${shopId}/reports/commission`, { callerPersonId: personId, from, to, format }));
  const serviceName = (id: string | null) => (id === null ? t("allServices") : services.find((s) => s.id === id)?.name ?? id);

  async function submit() {
    setNote("");
    const r = await save.run();
    if (r.ok) { setNote(t("ruleSaved")); setStaff(""); setServiceId(""); rules.reload(); }
  }

  async function report() {
    setReportNote("");
    const r = await download.run();
    if (r.ok) { saveFile(r.value); setReportNote(t("reportDone", { file: r.value.fileName })); }
  }

  return (
    <Stack>
      <Card title={t("rulesHeading")}>
        <Stack>
          <p>{t("rulesHint")}</p>
          {rules.error && <Notice kind="danger">{rules.error.message}</Notice>}
          {rules.loading && !rules.data ? <p>{t("loading")}</p> : (
            <Table
              rows={rules.data ?? []}
              rowKey={(r) => r.id}
              empty={t("rulesEmpty")}
              columns={[
                { header: t("colRuleStaff"), cell: (r) => r.staffId ?? t("allStaff") },
                { header: t("colRuleService"), cell: (r) => serviceName(r.serviceId) },
                { header: t("colPercent"), cell: (r) => `${r.percent}%`, align: "right" },
              ]}
            />
          )}
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <Inline>
                <TextField label={t("ruleStaff")} value={staff} onChange={(e) => setStaff(e.target.value)} />
                <SelectField label={t("ruleService")} value={serviceId} onChange={(e) => setServiceId(e.target.value)}>
                  <option value="">{t("allServices")}</option>
                  {services.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </SelectField>
                <TextField label={t("rulePercent")} type="number" min={0} max={100} value={percent} onChange={(e) => setPercent(e.target.value)} required />
                <Button type="submit" disabled={save.busy}>{t("saveRule")}</Button>
              </Inline>
              {save.error && <Notice kind="danger">{save.error.message}</Notice>}
              {note && <Notice kind="success">{note}</Notice>}
            </Stack>
          </form>
        </Stack>
      </Card>
      <Card title={t("reportHeading")}>
        <Stack>
          <Inline>
            <TextField label={t("reportFrom")} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <TextField label={t("reportTo")} type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            <SelectField label={t("reportFormat")} value={format} onChange={(e) => setFormat(e.target.value)}>
              <option value="csv">CSV</option>
              <option value="xlsx">Excel</option>
              <option value="pdf">PDF</option>
            </SelectField>
            <Button disabled={download.busy} onClick={() => void report()}>{t("reportDownload")}</Button>
          </Inline>
          {download.error && <Notice kind="danger">{download.error.message}</Notice>}
          {reportNote && <Notice kind="success">{reportNote}</Notice>}
        </Stack>
      </Card>
    </Stack>
  );
}
