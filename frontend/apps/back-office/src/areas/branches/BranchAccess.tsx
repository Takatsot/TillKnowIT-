import { useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Notice, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

/** POST /organisations/{shopId}/all-branches-access: lets one person manage every branch of the head shop. */
export function BranchAccess() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [person, setPerson] = useState("");
  const [done, setDone] = useState(false);
  const grant = useAction(() => api.post<unknown>(`/organisations/${shopId}/all-branches-access`, { callerPersonId: personId, personId: person.trim() }));

  async function submit() {
    setDone(false);
    const r = await grant.run();
    if (r.ok) {
      setDone(true);
      setPerson("");
    }
  }

  return (
    <Card title={t("accessHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("accessExplain")}</p>
          <TextField label={t("accessPerson")} value={person} onChange={(e) => setPerson(e.target.value)} required />
          <div><Button type="submit" disabled={grant.busy || person.trim() === ""}>{t("accessGrant")}</Button></div>
          {grant.error && <Notice kind="danger">{grant.error.code === "plan.feature_not_included" ? t("accessNote") : grant.error.message}</Notice>}
          {done && <Notice kind="success">{t("accessDone")}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}
