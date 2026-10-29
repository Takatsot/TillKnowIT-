import { useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { fail, ok } from "@tillknowit/domain";
import { Button, Card, Inline, Notice, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { HoldersTab } from "./HoldersTab";
import { MenuTab } from "./MenuTab";
import { MoneyTab } from "./MoneyTab";
import { PayoutsTab, ReconciliationTab } from "./PayoutsTab";
import { SchoolTab } from "./SchoolTab";
import { messages, type SchoolsKey } from "./messages";
import type { HolderUploader, School } from "./types";

const tabs = [
  { id: "school", label: "tabSchool", needsSchool: false },
  { id: "holders", label: "tabHolders", needsSchool: true },
  { id: "menu", label: "tabMenu", needsSchool: true },
  { id: "money", label: "tabMoney", needsSchool: true },
  { id: "payouts", label: "tabPayouts", needsSchool: false },
  { id: "reconciliation", label: "tabReconciliation", needsSchool: false },
] as const satisfies ReadonlyArray<{ id: string; label: SchoolsKey; needsSchool: boolean }>;

/** Sends the import as a form, because the server reads a file upload. Gives the server's own code and message when it refuses. */
export const uploadHolders: HolderUploader = async (schoolId, callerPersonId, file) => {
  const form = new FormData();
  form.append("callerPersonId", callerPersonId);
  form.append("file", file);
  let response: Response;
  try {
    response = await fetch(`/api/v1/schools/${schoolId}/holders/import`, { method: "POST", body: form });
  } catch {
    return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
  }
  if (!response.ok) {
    try {
      const body = (await response.json()) as { code?: string; title?: string };
      return fail(body.code ?? `http.${response.status}`, body.title ?? `The server answered ${response.status}.`);
    } catch {
      return fail(`http.${response.status}`, `The server answered ${response.status}.`);
    }
  }
  return ok(await response.json());
};

export function SchoolsWallet({ upload = uploadHolders }: { upload?: HolderUploader }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [school, setSchool] = useState<School | null>(null);
  const [schoolId, setSchoolId] = useState("");
  const [tab, setTab] = useState<(typeof tabs)[number]["id"]>("school");
  const [note, setNote] = useState("");
  const find = useAction(() => api.get<School>(`/schools/${schoolId.trim()}`, { callerPersonId: personId }));

  async function open() {
    const r = await find.run();
    if (r.ok) {
      setSchool(r.value);
      setNote(t("opened", { name: r.value.name }));
    }
  }

  function opened(s: School) {
    setSchool(s);
    setSchoolId(s.id);
    setNote("");
  }

  return (
    <Stack>
      <Card title={t("openHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); void open(); }}>
          <Stack>
            <p>{t("openIntro")}</p>
            <Inline>
              <TextField label={t("schoolId")} value={schoolId} onChange={(e) => setSchoolId(e.target.value)} required />
              <Button type="submit" disabled={find.busy || schoolId.trim() === ""}>{t("open")}</Button>
            </Inline>
            {find.error && <Notice kind="danger">{find.error.message}</Notice>}
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
        {tab === "holders" && (school ? <HoldersTab key={school.id} school={school} upload={upload} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "menu" && (school ? <MenuTab key={school.id} school={school} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "money" && (school ? <MoneyTab key={school.id} school={school} /> : <Notice kind="info">{t("noSchool")}</Notice>)}
        {tab === "payouts" && <PayoutsTab defaultShopId={school?.shopId ?? ""} />}
        {tab === "reconciliation" && <ReconciliationTab />}
      </div>
    </Stack>
  );
}
