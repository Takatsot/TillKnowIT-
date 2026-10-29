import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { messages } from "./messages";

export function Settings() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const sharing = useLoad(() => api.get<{ mode: string }>(`/shops/${shopId}/sharing`, { callerPersonId: personId }), [shopId, personId]);
  const [mode, setMode] = useState("off");
  const [saved, setSaved] = useState("");
  useEffect(() => { if (sharing.data) setMode(sharing.data.mode); }, [sharing.data]);

  const save = useAction(() => api.put<{ mode: string }>(`/shops/${shopId}/sharing`, { callerPersonId: personId, mode }));

  async function submit() {
    setSaved("");
    const r = await save.run();
    if (r.ok) setSaved(t("sharingSaved"));
  }

  return (
    <Stack>
      <Card title={t("sharingHeading")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            {sharing.error && <Notice kind="danger">{sharing.error.message}</Notice>}
            <Inline>
              <SelectField label={t("sharingMode")} hint={t("sharingHint")} value={mode} onChange={(e) => setMode(e.target.value)}>
                <option value="off">{t("sharingOff")}</option>
                <option value="usual">{t("sharingUsual")}</option>
                <option value="all">{t("sharingAll")}</option>
              </SelectField>
              <Button type="submit" disabled={save.busy || sharing.loading}>{t("sharingSave")}</Button>
            </Inline>
            {save.error && <Notice kind="danger">{save.error.message}</Notice>}
            {saved && <Notice kind="success">{saved}</Notice>}
          </Stack>
        </form>
      </Card>
      <Card title={t("usualHeading")}>
        <p>{t("usualHint")}</p>
      </Card>
    </Stack>
  );
}
