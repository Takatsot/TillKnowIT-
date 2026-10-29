import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { formatRand, newId } from "@tillknowit/domain";
import { useState } from "react";
import type { TillContext } from "../../feature";
import { ErrorNotice, TabGate, useAction, useKitT, useLoad, useT, who, type Caller, type Translate } from "../till-kit/kit";
import { addDays, saDate, saInstant, saTime } from "../till-kit/time";
import { findCustomers, type Customer } from "../customers/customerApi";
import { messages, type AppointmentsKey } from "./messages";

interface Service {
  id: string;
  name: string;
  durationMinutes: number;
  priceCents: number;
  isActive: boolean;
  staffIds: string[];
}

interface Appointment {
  id: string;
  serviceId: string;
  serviceName: string;
  staffId: string;
  customerId: string | null;
  customerName: string;
  customerPhone: string | null;
  startsAt: string;
  endsAt: string;
  status: string;
  hasConflict: boolean;
  priceCents: number;
  notes: string | null;
}

const actionsFor = (status: string): string[] => (status === "booked" ? ["confirm", "complete", "cancel", "no_show"] : status === "confirmed" ? ["complete", "cancel", "no_show"] : []);
const statusKey = (s: string): AppointmentsKey => (`status.${s.replace("_", "")}` as AppointmentsKey);

export function Appointments({ ctx }: { ctx: TillContext }) {
  return <TabGate ctx={ctx}>{(caller) => <AppointmentsBody ctx={ctx} caller={caller} />}</TabGate>;
}

/** Names for the people who can do a service: the server sends only their ids, so the signed-in person is named and the others are numbered. */
function staffName(ctx: TillContext, t: Translate<AppointmentsKey>, id: string, index: number): string {
  return id === ctx.staff.id ? t("who.me", { name: ctx.staff.name }) : t("who.other", { n: index + 1 });
}

function AppointmentsBody({ ctx, caller }: { ctx: TillContext; caller: Caller }) {
  const t = useT(messages, ctx.language);
  const kit = useKitT(ctx.language);
  const [day, setDay] = useState(() => saDate());
  const [booking, setBooking] = useState(false);
  const [info, setInfo] = useState("");
  const action = useAction();
  const services = useLoad(() => ctx.api.get<Service[]>(`/shops/${caller.shopId}/services`, who(caller)), [caller.shopId]);
  const list = useLoad(
    () => ctx.api.get<Appointment[]>(`/shops/${caller.shopId}/appointments`, { ...who(caller), from: saInstant(day, "00:00"), to: saInstant(addDays(day, 1), "00:00") }),
    [caller.shopId, day],
  );

  const act = async (a: Appointment, name: string) => {
    const r = await action.run(() => ctx.api.post<Appointment>(`/shops/${caller.shopId}/appointments/${a.id}/action`, { ...who(caller), action: name }));
    if (r.ok) {
      setInfo(t("act.done", { customer: a.customerName, status: t(statusKey(r.value.status)) }));
      list.reload();
    }
  };

  if (booking) {
    return (
      <Book
        ctx={ctx} caller={caller} t={t} services={(services.data ?? []).filter((s) => s.isActive)} day={day} back={() => setBooking(false)}
        booked={(a) => { setInfo(t("book.saved", { service: a.serviceName, customer: a.customerName, time: `${saTime(a.startsAt)}` })); setBooking(false); list.reload(); }}
      />
    );
  }

  const rows = [...(list.data ?? [])].sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const staffIds = [...new Set((services.data ?? []).flatMap((s) => s.staffIds))];
  const withName = (id: string) => staffName(ctx, t, id, Math.max(0, staffIds.indexOf(id)));

  return (
    <Card title={t("title")}>
      <Stack>
        <Inline>
          <TextField label={t("day.label")} type="date" value={day} onChange={(e) => e.target.value && setDay(e.target.value)} />
          <Button variant="secondary" onClick={() => setDay(saDate())}>{t("day.today")}</Button>
          <Button onClick={() => setBooking(true)}>{t("list.book")}</Button>
        </Inline>
        {info ? <Notice kind="success">{info}</Notice> : null}
        <ErrorNotice error={list.error ?? action.error} t={kit} />
        {list.loading && list.data === null ? <p>{kit("kit.loading")}</p> : null}
        {list.data ? (
          <Table
            empty={t("list.empty")}
            rows={rows}
            rowKey={(a) => a.id}
            columns={[
              { header: t("list.colTime"), cell: (a) => `${saTime(a.startsAt)} - ${saTime(a.endsAt)}` },
              { header: t("list.colService"), cell: (a) => a.serviceName },
              { header: t("list.colCustomer"), cell: (a) => (<>{a.customerName}{a.customerPhone ? ` ${a.customerPhone}` : ""}{a.hasConflict ? <div><Badge>{t("list.conflict")}</Badge></div> : null}</>) },
              { header: t("list.colWith"), cell: (a) => withName(a.staffId) },
              { header: t("list.colPrice"), cell: (a) => formatRand(a.priceCents), align: "right" },
              { header: t("list.colStatus"), cell: (a) => <Badge>{t(statusKey(a.status))}</Badge> },
              {
                header: t("list.colActions"),
                cell: (a) => (
                  <Inline>
                    {actionsFor(a.status).map((name) => (
                      <Button key={name} variant={name === "complete" || name === "confirm" ? "primary" : "secondary"} disabled={action.busy} aria-label={t(`act.${name}` as AppointmentsKey, { customer: a.customerName })} onClick={() => void act(a, name)}>
                        {t(`act.${name}` as AppointmentsKey, { customer: a.customerName })}
                      </Button>
                    ))}
                  </Inline>
                ),
              },
            ]}
          />
        ) : null}
      </Stack>
    </Card>
  );
}

function Book({ ctx, caller, t, services, day, back, booked }: { ctx: TillContext; caller: Caller; t: Translate<AppointmentsKey>; services: Service[]; day: string; back: () => void; booked: (a: Appointment) => void }) {
  const kit = useKitT(ctx.language);
  const [serviceId, setServiceId] = useState("");
  const [providerId, setProviderId] = useState("");
  const [date, setDate] = useState(day < saDate() ? saDate() : day);
  const [time, setTime] = useState("");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [search, setSearch] = useState("");
  const [found, setFound] = useState<Customer[]>([]);
  const [problem, setProblem] = useState("");
  const [id] = useState(() => newId());
  const action = useAction();
  const lookup = useAction();

  const service = services.find((s) => s.id === serviceId);
  const allStaff = [...new Set(services.flatMap((s) => s.staffIds))];
  const providers = service?.staffIds ?? [];

  async function find() {
    const r = await lookup.run(() => findCustomers(ctx.api, caller, search));
    if (r.ok) setFound(r.value);
  }

  async function save() {
    if (!service) return setProblem(t("book.serviceRequired"));
    if (providerId === "") return setProblem(t("book.staffRequired"));
    if (date === "" || time === "") return setProblem(t("book.timeRequired"));
    if (name.trim() === "" && !customer) return setProblem(t("book.customerRequired"));
    setProblem("");
    const r = await action.run(() =>
      ctx.api.post<Appointment>(`/shops/${caller.shopId}/appointments`, {
        ...who(caller), id, serviceId: service.id, providerStaffId: providerId, customerId: customer?.id ?? null, customerName: customer?.name ?? name.trim(),
        customerPhone: customer?.phone ?? (phone.trim() || null), startsAt: saInstant(date, time), notes: notes.trim() || null, allowConflict: false,
      }));
    if (r.ok) booked(r.value);
  }

  return (
    <Card title={t("book.title")}>
      <Stack>
        {services.length === 0 ? <Notice kind="warning">{t("book.noServices")}</Notice> : null}
        <SelectField label={t("book.service")} value={serviceId} onChange={(e) => { setServiceId(e.target.value); setProviderId(""); }}>
          <option value="">{t("book.serviceChoose")}</option>
          {services.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </SelectField>
        {service ? <p>{t("book.duration", { minutes: service.durationMinutes, price: formatRand(service.priceCents) })}</p> : null}
        <SelectField label={t("book.staff")} value={providerId} onChange={(e) => setProviderId(e.target.value)}>
          <option value="">{t("book.staffChoose")}</option>
          {providers.map((p) => <option key={p} value={p}>{staffName(ctx, t, p, allStaff.indexOf(p))}</option>)}
        </SelectField>
        <Inline>
          <TextField label={t("book.date")} type="date" min={saDate()} value={date} onChange={(e) => setDate(e.target.value)} />
          <TextField label={t("book.time")} hint={t("book.timeHint")} type="time" step={300} value={time} onChange={(e) => setTime(e.target.value)} />
        </Inline>

        {ctx.features.isOn("customers") ? (
          <Stack>
            <TextField label={t("book.find")} hint={t("book.findHint")} value={search} onChange={(e) => setSearch(e.target.value)} autoComplete="off" />
            <Inline><Button variant="secondary" onClick={() => void find()} disabled={lookup.busy}>{t("book.findButton")}</Button></Inline>
            <ErrorNotice error={lookup.error} t={kit} />
            {found.map((c) => (
              <Inline key={c.id}><Button variant="secondary" aria-label={t("book.pick", { name: c.name })} onClick={() => { setCustomer(c); setFound([]); }}>{c.name} {c.phone}</Button></Inline>
            ))}
            {customer ? <Notice kind="success">{t("book.picked", { name: customer.name })}</Notice> : null}
          </Stack>
        ) : null}
        {customer ? null : (
          <>
            <TextField label={t("book.customer")} value={name} onChange={(e) => setName(e.target.value)} />
            <TextField label={t("book.phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </>
        )}
        <TextField label={t("book.notes")} maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} />
        {problem ? <Notice kind="danger">{problem}</Notice> : null}
        <ErrorNotice error={action.error} t={kit} />
        <Inline>
          <Button onClick={() => void save()} disabled={action.busy}>{t("book.save")}</Button>
          <Button variant="quiet" onClick={back}>{t("book.back")}</Button>
        </Inline>
      </Stack>
    </Card>
  );
}
