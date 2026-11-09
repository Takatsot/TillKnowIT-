import { useAction, useApi, useLoad, useMessages, useSession, useSetSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { languages } from "@tillknowit/i18n";
import { useState } from "react";
import { messages, type GettingStartedKey } from "./messages";

interface Lookup { code: string; names: Record<string, string>; isActive: boolean }
interface Checklist { steps: Array<{ item: string; done: boolean; note: string | null }>; allDone: boolean; shopStatus: string }

/** A new owner makes their shop here; after that this shows how far the field agent's setup has got. */
export function GettingStarted({ reload = () => window.location.reload() }: { reload?: () => void }) {
  const { shopId } = useSession();
  return shopId === "" ? <CreateShop reload={reload} /> : <Progress />;
}

function CreateShop({ reload }: { reload: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, language } = useSession();
  const setSession = useSetSession();
  const municipalities = useLoad(() => api.get<Lookup[]>("/lookups/municipalities"), []);
  const [name, setName] = useState("");
  const [area, setArea] = useState("");
  const [shopLanguage, setShopLanguage] = useState<string>(language);
  const [problem, setProblem] = useState("");
  const [done, setDone] = useState("");
  const create = useAction(() => api.post<{ shopId: string }>(`/shops?ownerPersonId=${personId}`, { name: name.trim(), area, language: shopLanguage }));

  async function submit() {
    setProblem("");
    if (name.trim() === "") { setProblem(t("nameRequired")); return; }
    const r = await create.run();
    if (!r.ok) return;
    setDone(t("created", { name: name.trim() }));
    setSession({ shopId: r.value.shopId });
    // The menu's "working for" list was loaded before the shop existed: loading the page again is what refreshes it.
    reload();
  }

  return (
    <Card title={t("createHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }} noValidate>
        <Stack>
          <p>{t("createIntro")}</p>
          <TextField label={t("shopName")} value={name} error={problem} onChange={(e) => setName(e.target.value)} required />
          <SelectField label={t("area")} hint={t("areaHint")} value={area} onChange={(e) => setArea(e.target.value)}>
            <option value="">{t("areaChoose")}</option>
            {(municipalities.data ?? []).filter((m) => m.isActive).map((m) => <option key={m.code} value={m.code}>{m.names[language] ?? m.names.en ?? m.code}</option>)}
          </SelectField>
          <SelectField label={t("language")} value={shopLanguage} onChange={(e) => setShopLanguage(e.target.value)}>
            {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </SelectField>
          {create.error && <Notice kind="danger">{create.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
          <div><Button type="submit" disabled={create.busy}>{t("create")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

function Progress() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const list = useLoad(() => api.get<Checklist>(`/shops/${shopId}/setup`), [shopId]);
  const data = list.data;
  const label = (item: string) => {
    const key = `step_${item}`;
    return Object.hasOwn(messages.en, key) ? t(key as GettingStartedKey) : item.replace(/_/g, " ");
  };
  const done = data?.steps.filter((s) => s.done).length ?? 0;
  const verified = data?.shopStatus === "verified";

  return (
    <Stack>
      <Card title={t("progressHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !data ? <p>{t("loading")}</p> : data && (
          <Stack>
            <Badge>{t(verified ? "statusVerified" : data.shopStatus === "suspended" ? "statusSuspended" : "statusPending")}</Badge>
            <p><strong>{t("progress", { done, total: data.steps.length })}</strong></p>
            <Notice kind={verified ? "success" : "info"}>{t(verified ? "verifiedHelp" : "pendingHelp")}</Notice>
          </Stack>
        )}
      </Card>
      {data && (
        <Card title={t("stepsHeading")}>
          <Table
            rows={data.steps}
            rowKey={(s) => s.item}
            columns={[
              { header: t("colStep"), cell: (s) => label(s.item) },
              { header: t("colState"), cell: (s) => <Badge>{s.done ? t("done") : t("notDone")}</Badge> },
            ]}
          />
        </Card>
      )}
      <Card title={t("nextHeading")}>
        <ul>
          <li>{t("next1")}</li>
          <li>{t("next2")}</li>
          <li>{t("next3")}</li>
        </ul>
      </Card>
    </Stack>
  );
}
