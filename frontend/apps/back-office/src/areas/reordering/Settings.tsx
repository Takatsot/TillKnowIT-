import { formatDate, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { messages } from "./messages";

interface UsualSupplier { supplierId: string; name: string; lastOrderAt: string | null }

export function Settings() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const sharing = useLoad(() => api.get<{ mode: string }>(`/shops/${shopId}/sharing`, { callerPersonId: personId }), [shopId, personId]);
  const usualList = useLoad(() => api.get<UsualSupplier[]>(`/shops/${shopId}/usual-suppliers`, { callerPersonId: personId }), [shopId, personId]);
  const [mode, setMode] = useState("off");
  const [saved, setSaved] = useState("");
  useEffect(() => { if (sharing.data) setMode(sharing.data.mode); }, [sharing.data]);

  const save = useAction(() => api.put<{ mode: string }>(`/shops/${shopId}/sharing`, { callerPersonId: personId, mode }));

  const [removed, setRemoved] = useState("");
  const remove = useAction((supplierId: string) => api.delete<unknown>(`/shops/${shopId}/usual-suppliers/${supplierId}`, { callerPersonId: personId }));

  async function removeUsual(s: UsualSupplier) {
    setRemoved("");
    const r = await remove.run(s.supplierId);
    if (r.ok) {
      setRemoved(t("usualRemoved", { name: s.name }));
      usualList.reload();
    }
  }

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
        <Stack>
          <p>{t("usualHint")}</p>
          {usualList.error && <Notice kind="danger">{usualList.error.message}</Notice>}
          {usualList.loading ? <p role="status">{t("usualLoading")}</p> : usualList.data && usualList.data.length === 0 ? <p>{t("usualEmpty")}</p> : (
            <ul aria-label={t("usualHeading")}>
              {(usualList.data ?? []).map((s) => (
                <li key={s.supplierId}>
                  {s.name}: {s.lastOrderAt ? t("usualLastOrder", { date: formatDate(s.lastOrderAt) }) : t("usualNeverOrdered")}{" "}
                  <Button type="button" aria-label={t("usualRemove", { name: s.name })} disabled={remove.busy} onClick={() => void removeUsual(s)}>{t("usualRemoveLabel")}</Button>
                </li>
              ))}
            </ul>
          )}
          {remove.error && <Notice kind="danger">{remove.error.message}</Notice>}
          {removed && <Notice kind="success">{removed}</Notice>}
        </Stack>
      </Card>
    </Stack>
  );
}
