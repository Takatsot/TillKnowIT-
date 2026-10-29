import { formatDate, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type GroupsKey } from "./messages";

export interface Group { id: string; name: string; kind: string; adminShopId: string; members: number; youAreAdmin: boolean }
export interface Invite { groupId: string; code: string; expiresAt: string }
export interface GroupDeal {
  id: string; supplierName: string; productName: string; totalCases: number; minTotalCases: number; closesAt: string; status: string; yourCases: number | null;
}
export interface Member { shopId: string; shopName: string; joinedAt: string; casesOnOpenDeals: number | null }
export interface History { groupId: string; name: string; deals: GroupDeal[]; members: Member[] | null }

export function BuyingGroups() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const groups = useLoad(() => api.get<Group[]>(`/shops/${shopId}/buying-groups`, { callerPersonId: personId }), [shopId, personId]);
  const [notice, setNotice] = useState("");
  const [invite, setInvite] = useState<{ name: string; invite: Invite } | null>(null);
  const [historyOf, setHistoryOf] = useState<Group | null>(null);

  const makeInvite = useAction((g: Group) => api.post<Invite>(`/buying-groups/${g.id}/invites`, undefined, { callerPersonId: personId }));
  const leave = useAction((g: Group) => api.post<unknown>(`/shops/${shopId}/buying-groups/${g.id}/leave`, undefined, { callerPersonId: personId }));

  async function doInvite(g: Group) {
    setNotice("");
    const r = await makeInvite.run(g);
    if (r.ok) setInvite({ name: g.name, invite: r.value });
  }

  async function doLeave(g: Group) {
    setNotice("");
    const r = await leave.run(g);
    if (r.ok) {
      setNotice(t("left", { name: g.name }));
      if (historyOf?.id === g.id) setHistoryOf(null);
      groups.reload();
    }
  }

  const kindLabel = (k: string) => ({ Stokvel: t("kindStokvel"), School: t("kindSchool"), Other: t("kindOther") })[k] ?? k;
  const error = makeInvite.error ?? leave.error;

  return (
    <Stack>
      {notice && <Notice kind="success">{notice}</Notice>}
      <Card title={t("groupsHeading")}>
        <Stack>
          {groups.error && <Notice kind="danger">{groups.error.message}</Notice>}
          {error && <Notice kind="danger">{error.message}</Notice>}
          {invite && (
            <Notice kind="info">
              <strong>{t("inviteHeading", { name: invite.name })}</strong>
              <div>{t("inviteCode", { code: invite.invite.code })}</div>
              <div>{t("inviteNote", { date: formatDate(invite.invite.expiresAt) })}</div>
            </Notice>
          )}
          {groups.loading && !groups.data && !groups.error ? <p>{t("loading")}</p> : (
            <Table
              rows={groups.data ?? []}
              rowKey={(g) => g.id}
              empty={groups.error ? undefined : t("groupsEmpty")}
              columns={[
                { header: t("colName"), cell: (g) => g.name },
                { header: t("colKind"), cell: (g) => kindLabel(g.kind) },
                { header: t("colMembers"), align: "right", cell: (g) => g.members },
                { header: t("colRole"), cell: (g) => <Badge>{g.youAreAdmin ? t("roleAdmin") : t("roleMember")}</Badge> },
                {
                  header: "",
                  cell: (g) => (
                    <Inline>
                      <Button variant="secondary" onClick={() => setHistoryOf(historyOf?.id === g.id ? null : g)}>{t(historyOf?.id === g.id ? "closeHistory" : "openHistory", { name: g.name })}</Button>
                      {g.youAreAdmin && <Button variant="secondary" disabled={makeInvite.busy} onClick={() => void doInvite(g)}>{t("invite", { name: g.name })}</Button>}
                      {!g.youAreAdmin && <Button variant="quiet" disabled={leave.busy} onClick={() => void doLeave(g)}>{t("leave", { name: g.name })}</Button>}
                    </Inline>
                  ),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      {historyOf && <HistoryCard group={historyOf} onChanged={() => groups.reload()} onNotice={setNotice} />}
      <CreateCard onDone={(text) => { setNotice(text); groups.reload(); }} />
      <JoinCard onDone={(text) => { setNotice(text); groups.reload(); }} />
    </Stack>
  );
}

function HistoryCard({ group, onChanged, onNotice }: { group: Group; onChanged: () => void; onNotice: (s: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const history = useLoad(() => api.get<History>(`/shops/${shopId}/buying-groups/${group.id}/history`, { callerPersonId: personId }), [group.id, shopId, personId]);
  const remove = useAction((m: Member) => api.delete<unknown>(`/buying-groups/${group.id}/members/${m.shopId}`, { callerPersonId: personId }));

  async function doRemove(m: Member) {
    onNotice("");
    const r = await remove.run(m);
    if (r.ok) {
      onNotice(t("removed", { name: m.shopName }));
      history.reload();
      onChanged();
    }
  }

  const data = history.data;
  return (
    <Card title={t("historyHeading", { name: group.name })}>
      <Stack>
        {history.error && <Notice kind="danger">{history.error.message}</Notice>}
        {remove.error && <Notice kind="danger">{remove.error.message}</Notice>}
        {history.loading && !data && !history.error && <p>{t("loading")}</p>}
        {data && (
          <>
            <Table
              rows={data.deals}
              rowKey={(d) => d.id}
              empty={t("dealsEmpty")}
              columns={[
                { header: t("colProduct"), cell: (d) => d.productName },
                { header: t("colSupplier"), cell: (d) => d.supplierName },
                { header: t("colStatus"), cell: (d) => <Badge>{d.status}</Badge> },
                { header: t("colCases"), align: "right", cell: (d) => `${d.totalCases} / ${d.minTotalCases}` },
                { header: t("colYours"), align: "right", cell: (d) => d.yourCases ?? "" },
                { header: t("colCloses"), cell: (d) => formatDate(d.closesAt) },
              ]}
            />
            {data.members && (
              <>
                <h3>{t("membersHeading")}</h3>
                <Table
                  rows={data.members}
                  rowKey={(m) => m.shopId}
                  columns={[
                    { header: t("colShop"), cell: (m) => m.shopName },
                    { header: t("colJoined"), cell: (m) => formatDate(m.joinedAt) },
                    { header: t("colOpenCases"), align: "right", cell: (m) => m.casesOnOpenDeals ?? "" },
                    { header: "", cell: (m) => m.shopId === group.adminShopId ? "" : <Button variant="quiet" disabled={remove.busy} onClick={() => void doRemove(m)}>{t("removeMember", { name: m.shopName })}</Button> },
                  ]}
                />
              </>
            )}
          </>
        )}
      </Stack>
    </Card>
  );
}

function CreateCard({ onDone }: { onDone: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [name, setName] = useState("");
  const [kind, setKind] = useState("Stokvel");
  const create = useAction(() => api.post<Group>("/buying-groups", { callerPersonId: personId, shopId, name: name.trim(), kind }));

  async function submit() {
    const r = await create.run();
    if (r.ok) {
      setName("");
      onDone(t("created", { name: r.value.name }));
    }
  }

  const kinds: [string, GroupsKey][] = [["Stokvel", "kindStokvel"], ["School", "kindSchool"], ["Other", "kindOther"]];
  return (
    <Card title={t("createHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("createExplain")}</p>
          <Inline>
            <TextField label={t("groupName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <SelectField label={t("groupKind")} value={kind} onChange={(e) => setKind(e.target.value)}>
              {kinds.map(([v, k]) => <option key={v} value={v}>{t(k)}</option>)}
            </SelectField>
          </Inline>
          {create.error && <Notice kind="danger">{create.error.message}</Notice>}
          <div><Button type="submit" disabled={create.busy || name.trim() === ""}>{t("create")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}

function JoinCard({ onDone }: { onDone: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const [code, setCode] = useState("");
  const join = useAction(() => api.post<Group>(`/shops/${shopId}/buying-groups/join`, { callerPersonId: personId, code: code.trim() }));

  async function submit() {
    const r = await join.run();
    if (r.ok) {
      setCode("");
      onDone(t("joined", { name: r.value.name }));
    }
  }

  return (
    <Card title={t("joinHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <TextField label={t("joinCode")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" required />
          {join.error && <Notice kind="danger">{join.error.message}</Notice>}
          <div><Button type="submit" disabled={join.busy || code.trim() === ""}>{t("join")}</Button></div>
        </Stack>
      </form>
    </Card>
  );
}
