import { useAction, useLoad, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { BranchAccess } from "./BranchAccess";
import { messages } from "./messages";
import { Transfers } from "./Transfers";

export interface Branch {
  shopId: string;
  name: string;
  isHead: boolean;
  status: string;
  municipalityCode: string | null;
}

function ShopsTab() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const list = useLoad(() => api.get<Branch[]>(`/organisations/${shopId}/branches`, { callerPersonId: personId }), [shopId, personId]);
  const [name, setName] = useState("");
  const [area, setArea] = useState("");
  const [added, setAdded] = useState("");

  const add = useAction(() => api.post<Branch>(`/organisations/${shopId}/branches`, { callerPersonId: personId, name, area: area.trim() === "" ? null : area.trim() }));

  async function submit() {
    const r = await add.run();
    if (r.ok) {
      setAdded(t("added", { name: r.value.name }));
      setName("");
      setArea("");
      list.reload();
    }
  }

  return (
    <Stack>
      <Card title={t("heading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(b) => b.shopId}
            empty={t("empty")}
            columns={[
              { header: t("colName"), cell: (b) => b.name },
              { header: t("colKind"), cell: (b) => <Badge>{b.isHead ? t("head") : t("branch")}</Badge> },
              { header: t("colArea"), cell: (b) => b.municipalityCode ?? "" },
              { header: t("status"), cell: (b) => b.status },
            ]}
          />
        )}
      </Card>
      <Card title={t("add")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <Inline>
              <TextField label={t("name")} value={name} onChange={(e) => setName(e.target.value)} required />
              <TextField label={t("area")} hint={t("areaHint")} value={area} onChange={(e) => setArea(e.target.value)} />
              <Button type="submit" disabled={add.busy || name.trim() === ""}>{t("create")}</Button>
            </Inline>
            {add.error && <Notice kind="danger">{add.error.code === "plan.feature_not_included" ? t("planNote") : add.error.message}</Notice>}
            {added && <Notice kind="success">{added}</Notice>}
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}

export function Branches() {
  const t = useMessages(messages);
  const [tab, setTab] = useState<"shops" | "access" | "transfers">("shops");
  const tabs = [["shops", t("tabShops")], ["access", t("tabAccess")], ["transfers", t("tabTransfers")]] as const;
  return (
    <Stack>
      <nav aria-label={t("title")}>
        <Inline>
          {tabs.map(([id, label]) => <Button key={id} variant={tab === id ? "primary" : "secondary"} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}
        </Inline>
      </nav>
      {tab === "shops" && <ShopsTab />}
      {tab === "access" && <BranchAccess />}
      {tab === "transfers" && <Transfers />}
    </Stack>
  );
}
