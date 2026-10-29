import { useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { HoldersTab } from "./HoldersTab";
import { MenuTab } from "./MenuTab";
import { MoneyTab } from "./MoneyTab";
import { PayoutsTab, ReconciliationTab } from "./PayoutsTab";
import { SchoolTab } from "./SchoolTab";
import { messages, type SchoolsKey } from "./messages";
import type { HolderUploader, ImportResult, School } from "./types";

const tabs = [
  { id: "school", label: "tabSchool", needsSchool: false },
  { id: "holders", label: "tabHolders", needsSchool: true },
  { id: "menu", label: "tabMenu", needsSchool: true },
  { id: "money", label: "tabMoney", needsSchool: true },
  { id: "payouts", label: "tabPayouts", needsSchool: false },
  { id: "reconciliation", label: "tabReconciliation", needsSchool: false },
] as const satisfies ReadonlyArray<{ id: string; label: SchoolsKey; needsSchool: boolean }>;

/** `upload` replaces the holders import in tests; by default the import goes through Api.upload. */
export function SchoolsWallet({ upload }: { upload?: HolderUploader }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const schools = useLoad(() => api.get<School[]>("/admin/schools", { callerPersonId: personId }), [personId]);
  const [school, setSchool] = useState<School | null>(null);
  const [schoolId, setSchoolId] = useState("");
  const [tab, setTab] = useState<(typeof tabs)[number]["id"]>("school");
  const [note, setNote] = useState("");

  const sendHolders: HolderUploader = upload ?? ((sid, callerPersonId, file) => {
    const form = new FormData();
    form.append("callerPersonId", callerPersonId);
    form.append("file", file);
    return api.upload<ImportResult>(`/schools/${sid}/holders/import`, form);
  });

  function open() {
    const found = (schools.data ?? []).find((s) => s.id === schoolId);
    if (!found) return;
    setSchool(found);
    setNote(t("opened", { name: found.name }));
  }

  function opened(s: School) {
    setSchool(s);
    setSchoolId(s.id);
    setNote("");
    schools.reload();
  }

  return (
    <Stack>
      <Card title={t("openHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); open(); }}>
          <Stack>
            <p>{t("openIntro")}</p>
            {schools.error && <Notice kind="danger">{schools.error.message}</Notice>}
            {!schools.error && !schools.loading && (schools.data ?? []).length === 0 && <Notice kind="info">{t("noSchools")}</Notice>}
            <Inline>
              <SelectField label={t("schoolPick")} value={schoolId} onChange={(e) => setSchoolId(e.target.value)}>
                <option value="">{t("pickSchool")}</option>
                {(schools.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </SelectField>
              <Button type="submit" disabled={schoolId === ""}>{t("open")}</Button>
            </Inline>
            {note && <Notice kind="success">{note}</Notice>}
          </Stack>
        </form>
      </Card>
      <div role="tablist" aria-label={t("tabs")}>
        <Inline>
          {tabs.map((x) => (
            <Button key={x.id} role="tab" aria-selected={tab === x.id} variant={tab === x.id ? "primary" : "secondary"} onClick={() => setTab(x.id)}>{t(x.label)}</Button>
          ))}
        </Inline>
      </div>
      <div role="tabpanel">
        {tab === "school" && <SchoolTab school={school} onSchool={opened} />}
        {tab === "holders" && (school ? <HoldersTab key={school.id} school={school} upload={sendHolders} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "menu" && (school ? <MenuTab key={school.id} school={school} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "money" && (school ? <MoneyTab key={school.id} school={school} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "payouts" && <PayoutsTab defaultShopId={school?.shopId ?? ""} />}
        {tab === "reconciliation" && <ReconciliationTab />}
      </div>
    </Stack>
  );
}
