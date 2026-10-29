import { isValidPin } from "@tillknowit/domain";
import { useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

interface StaffRow { id: string; name: string; role: string; disabled: boolean }
interface DeviceRow { id: string; name: string; status: "pending" | "approved" | "wiping" }

export function StaffDevices() {
  return (
    <Stack>
      <StaffCard />
      <DevicesCard />
    </Stack>
  );
}

function StaffCard() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [rows, setRows] = useState<StaffRow[]>([]);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [role, setRole] = useState("cashier");
  const [pin, setPin] = useState("");
  const [pinError, setPinError] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [pins, setPins] = useState<Record<string, string>>({});

  const add = useAction(() => api.post<{ membershipId: string }>(`/shops/${shopId}/staff`, { name: name.trim(), phone: phone.trim(), roleCode: role, pin }));
  const update = useAction((id: string, body: { pin?: string; disabled?: boolean }) => api.patch<void>(`/staff/${id}`, body));

  async function submit() {
    setNote("");
    if (!isValidPin(pin)) { setPinError(t("pinInvalid")); return; }
    setPinError(null);
    const r = await add.run();
    if (r.ok) {
      setRows((rs) => [...rs, { id: r.value.membershipId, name: name.trim(), role, disabled: false }]);
      setNote(t("staffAdded", { name: name.trim(), id: r.value.membershipId }));
      setName(""); setPhone(""); setPin("");
    }
  }

  async function changePin(row: StaffRow) {
    const next = pins[row.id] ?? "";
    setNote("");
    if (!isValidPin(next)) { setPinError(t("pinInvalid")); return; }
    setPinError(null);
    const r = await update.run(row.id, { pin: next });
    if (r.ok) { setPins((p) => ({ ...p, [row.id]: "" })); setNote(t("pinChanged", { name: row.name })); }
  }

  async function toggle(row: StaffRow) {
    setNote("");
    const r = await update.run(row.id, { disabled: !row.disabled });
    if (r.ok) setRows((rs) => rs.map((x) => (x.id === row.id ? { ...x, disabled: !row.disabled } : x)));
  }

  return (
    <Card title={t("staffHeading")}>
      <Stack>
        <p>{t("staffHint")}</p>
        {shopId === "" && <Notice kind="warning">{t("noShop")}</Notice>}
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <Inline>
              <TextField label={t("name")} value={name} onChange={(e) => setName(e.target.value)} required />
              <TextField label={t("phone")} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required />
              <SelectField label={t("role")} value={role} onChange={(e) => setRole(e.target.value)}>
                <option value="cashier">{t("cashier")}</option>
                <option value="manager">{t("manager")}</option>
              </SelectField>
              <TextField label={t("pin")} inputMode="numeric" autoComplete="off" value={pin} error={pinError} onChange={(e) => setPin(e.target.value)} required />
              <Button type="submit" disabled={add.busy || name.trim() === "" || phone.trim() === ""}>{t("addStaff")}</Button>
            </Inline>
            {add.error && <Notice kind="danger">{add.error.message}</Notice>}
            {note && <Notice kind="success">{note}</Notice>}
          </Stack>
        </form>
        {update.error && <Notice kind="danger">{update.error.message}</Notice>}
        <Table
          rows={rows}
          rowKey={(r) => r.id}
          empty={t("staffEmpty")}
          columns={[
            { header: t("colName"), cell: (r) => r.name },
            { header: t("colRole"), cell: (r) => (r.role === "manager" ? t("manager") : t("cashier")) },
            { header: t("colStatus"), cell: (r) => <Badge>{r.disabled ? t("disabled") : t("active")}</Badge> },
            { header: t("colId"), cell: (r) => <code>{r.id}</code> },
            {
              header: t("colActions"),
              cell: (r) => (
                <Inline>
                  <TextField label={t("newPin", { name: r.name })} inputMode="numeric" autoComplete="off" value={pins[r.id] ?? ""} onChange={(e) => setPins((p) => ({ ...p, [r.id]: e.target.value }))} />
                  <Button variant="secondary" disabled={update.busy} onClick={() => void changePin(r)}>{t("changePin", { name: r.name })}</Button>
                  <Button variant="quiet" disabled={update.busy} onClick={() => void toggle(r)}>{r.disabled ? t("enable", { name: r.name }) : t("disable", { name: r.name })}</Button>
                </Inline>
              ),
            },
          ]}
        />
      </Stack>
    </Card>
  );
}

function DevicesCard() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const [rows, setRows] = useState<DeviceRow[]>([]);
  const [name, setName] = useState("");
  const [note, setNote] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);

  const register = useAction(() => api.post<{ deviceId: string }>(`/shops/${shopId}/devices`, { name: name.trim() }));
  const act = useAction((path: string) => api.post<void>(path));

  async function submit() {
    setNote("");
    const r = await register.run();
    if (r.ok) {
      setRows((rs) => [...rs, { id: r.value.deviceId, name: name.trim(), status: "pending" }]);
      setNote(t("registered", { name: name.trim() }));
      setName("");
    }
  }

  async function approve(d: DeviceRow) {
    setNote("");
    const r = await act.run(`/devices/${d.id}/approve`);
    if (r.ok) { setRows((rs) => rs.map((x) => (x.id === d.id ? { ...x, status: "approved" } : x))); setNote(t("approvedNote", { name: d.name })); }
  }

  async function wipe(d: DeviceRow) {
    setNote("");
    setConfirming(null);
    const r = await act.run(`/devices/${d.id}/wipe`);
    if (r.ok) { setRows((rs) => rs.map((x) => (x.id === d.id ? { ...x, status: "wiping" } : x))); setNote(t("wipeRequested", { name: d.name })); }
  }

  const status = (d: DeviceRow) => (d.status === "pending" ? t("pending") : d.status === "approved" ? t("approved") : t("wiping"));

  return (
    <Card title={t("deviceHeading")}>
      <Stack>
        <p>{t("deviceHint")}</p>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <Inline>
              <TextField label={t("deviceName")} value={name} onChange={(e) => setName(e.target.value)} required />
              <Button type="submit" disabled={register.busy || name.trim() === "" || shopId === ""}>{t("register")}</Button>
            </Inline>
            {register.error && <Notice kind="danger">{register.error.message}</Notice>}
            {note && <Notice kind="success">{note}</Notice>}
          </Stack>
        </form>
        {act.error && <Notice kind="danger">{act.error.message}</Notice>}
        <Table
          rows={rows}
          rowKey={(d) => d.id}
          empty={t("devicesEmpty")}
          columns={[
            { header: t("colTill"), cell: (d) => d.name },
            { header: t("colTillStatus"), cell: (d) => <Badge>{status(d)}</Badge> },
            {
              header: t("colActions"),
              cell: (d) => (
                <Inline>
                  {d.status === "pending" && <Button disabled={act.busy} onClick={() => void approve(d)}>{t("approve", { name: d.name })}</Button>}
                  {confirming === d.id ? (
                    <>
                      <Button variant="secondary" disabled={act.busy} onClick={() => void wipe(d)}>{t("confirmWipe", { name: d.name })}</Button>
                      <Button variant="quiet" onClick={() => setConfirming(null)}>{t("keep")}</Button>
                    </>
                  ) : (
                    <Button variant="quiet" disabled={act.busy || d.status === "wiping"} onClick={() => setConfirming(d.id)}>{t("wipe", { name: d.name })}</Button>
                  )}
                </Inline>
              ),
            },
          ]}
        />
        {confirming && <Notice kind="warning">{t("wipeNote")}</Notice>}
        {rows.map((d) => (
          <Card key={d.id} title={t("setupHeading", { name: d.name })}>
            <p>{t("setupHint")}</p>
            <dl>
              <dt>{t("setupShop")}</dt><dd><code>{shopId}</code></dd>
              <dt>{t("setupDevice")}</dt><dd><code>{d.id}</code></dd>
              <dt>{t("setupJson")}</dt><dd><code>{JSON.stringify({ shopId, deviceId: d.id })}</code></dd>
            </dl>
          </Card>
        ))}
      </Stack>
    </Card>
  );
}
