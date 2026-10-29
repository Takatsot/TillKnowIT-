import { useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Card, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useState, type ReactNode } from "react";
import { messages } from "./messages";

export interface ShopDevice { deviceId: string; name: string; status: string; lastSeenAt: string | null; wipeRequested: boolean }
export interface ShopStaff { staffId: string; name: string; phone: string; role: string; disabled: boolean }
interface Membership { membershipId: string; organisationId: string; organisationName: string; organisationType: string; roleCode: string }

export interface TillCaller {
  deviceId: string;
  staffId: string;
  ready: boolean;
  /** The card that shows (and lets the person change) the till and staff member. Render it once on the screen. */
  card: ReactNode;
}

/**
 * Many selling routes want a till (deviceId) and a staff member (staffId) instead of a person. The till is chosen from the shop's approved tills
 * (GET /shops/{id}/devices). The staff id is the signed-in person's own membership at this shop (GET /people/{id}/memberships); when they have none
 * (for example a platform user) it is chosen from the shop's staff (GET /shops/{id}/staff).
 */
export function useTillCaller(): TillCaller {
  const api = useApi();
  const { personId, shopId } = useSession();
  const t = useMessages(messages);
  const [pickedDevice, setPickedDevice] = useState("");
  const [pickedStaff, setPickedStaff] = useState("");

  const devices = useLoad(() => (shopId === "" ? Promise.resolve({ ok: true as const, value: [] as ShopDevice[] }) : api.get<ShopDevice[]>(`/shops/${shopId}/devices`, { callerPersonId: personId })), [shopId, personId]);
  const memberships = useLoad(() => (personId === "" ? Promise.resolve({ ok: true as const, value: [] as Membership[] }) : api.get<Membership[]>(`/people/${personId}/memberships`)), [personId]);
  const own = memberships.data?.find((m) => m.organisationId === shopId)?.membershipId ?? "";
  const needStaff = !memberships.loading && own === "";
  const staff = useLoad(() => (needStaff && shopId !== "" ? api.get<ShopStaff[]>(`/shops/${shopId}/staff`, { callerPersonId: personId }) : Promise.resolve({ ok: true as const, value: [] as ShopStaff[] })), [shopId, personId, needStaff]);

  const approved = (devices.data ?? []).filter((d) => d.status === "approved");
  const staffRows = (staff.data ?? []).filter((s) => !s.disabled);
  const deviceId = approved.some((d) => d.deviceId === pickedDevice) ? pickedDevice : approved[0]?.deviceId ?? "";
  const staffId = own !== "" ? own : staffRows.some((s) => s.staffId === pickedStaff) ? pickedStaff : staffRows[0]?.staffId ?? "";
  const loading = devices.loading || memberships.loading || staff.loading;
  const error = devices.error ?? memberships.error ?? staff.error;

  const card = (
    <Card title={t("heading")}>
      <Stack>
        <p>{t("hint")}</p>
        {loading && <p>{t("loading")}</p>}
        {error && <Notice kind="danger">{t("loadFailed", { message: error.message })}</Notice>}
        {!loading && !devices.error && approved.length === 0 && <Notice kind="warning">{t("noDevice")}</Notice>}
        {approved.length > 0 && (
          <SelectField label={t("device")} value={deviceId} onChange={(e) => setPickedDevice(e.target.value)}>
            {approved.map((d) => <option key={d.deviceId} value={d.deviceId}>{d.name}</option>)}
          </SelectField>
        )}
        {own !== "" && <p>{t("actingAs", { id: own })}</p>}
        {needStaff && !loading && (
          <>
            {!memberships.error && <Notice kind="info">{t("noMembership")}</Notice>}
            {staffRows.length === 0 && !staff.error ? <Notice kind="warning">{t("noStaff")}</Notice> : (
              <SelectField label={t("staff")} value={staffId} onChange={(e) => setPickedStaff(e.target.value)}>
                {staffRows.map((s) => <option key={s.staffId} value={s.staffId}>{s.name}</option>)}
              </SelectField>
            )}
          </>
        )}
      </Stack>
    </Card>
  );
  return { deviceId, staffId, ready: deviceId !== "" && staffId !== "", card };
}
