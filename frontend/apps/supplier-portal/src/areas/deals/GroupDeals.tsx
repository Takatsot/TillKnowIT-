import { formatDateTime, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import { useProducts } from "./useProducts";

export interface GroupDeal {
  id: string; supplierProductId: string; productName: string; bulkPriceCents: number; minTotalCases: number; totalCases: number; progressPercent: number;
  closesAt: string; areaCode: string; status: string; shops: number | null; buyingGroupId: string | null;
}

function NewGroupDeal({ supplierId, onPosted }: { supplierId: string; onPosted: (name: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const products = useProducts(supplierId);
  const [product, setProduct] = useState("");
  const [price, setPrice] = useState("");
  const [min, setMin] = useState("");
  const [closes, setCloses] = useState("");
  const [area, setArea] = useState("");
  const [group, setGroup] = useState("");
  const [problem, setProblem] = useState("");
  const post = useAction((body: object) => api.post<GroupDeal>(`/suppliers/${supplierId}/group-deals`, body));

  async function submit() {
    const cents = parseRandToCents(price);
    const minCases = Number(min);
    if (cents === null) { setProblem(t("badMoney")); return; }
    if (!Number.isInteger(minCases) || minCases < 1 || closes === "") { setProblem(t("badNumber")); return; }
    setProblem("");
    const r = await post.run({
      callerPersonId: personId, supplierProductId: product, bulkPriceCents: cents, minTotalCases: minCases, closesAt: new Date(closes).toISOString(),
      areaCode: area.trim(), buyingGroupId: group.trim() === "" ? null : group.trim(),
    });
    if (r.ok) onPosted(r.value.productName);
  }

  return (
    <Card title={t("newGroup")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          {products.error && <Notice kind="danger">{products.error.message}</Notice>}
          <Inline>
            <SelectField label={t("fProduct")} value={product} onChange={(e) => setProduct(e.target.value)}>
              <option value="">{t("chooseProduct")}</option>
              {(products.data ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </SelectField>
            <TextField label={t("gBulk")} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
            <TextField label={t("gMin")} inputMode="numeric" value={min} onChange={(e) => setMin(e.target.value)} />
          </Inline>
          <Inline>
            <TextField label={t("gCloses")} type="datetime-local" value={closes} onChange={(e) => setCloses(e.target.value)} />
            <TextField label={t("gArea")} value={area} onChange={(e) => setArea(e.target.value)} />
            <TextField label={t("gGroup")} hint={t("gGroupHint")} value={group} onChange={(e) => setGroup(e.target.value)} />
          </Inline>
          <div><Button type="submit" disabled={post.busy || product === "" || area.trim() === ""}>{t("postGroup")}</Button></div>
          {problem && <Notice kind="danger">{problem}</Notice>}
          {post.error && <Notice kind="danger">{post.error.message}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

export function GroupDeals({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<GroupDeal[]>(`/suppliers/${supplierId}/group-deals`, { callerPersonId: personId }), [supplierId, personId]);
  const [note, setNote] = useState("");
  const act = useAction((g: GroupDeal, what: "close" | "cancel") => api.post<void>(`/group-deals/${g.id}/${what}`, undefined, { callerPersonId: personId }));

  async function run(g: GroupDeal, what: "close" | "cancel") {
    const r = await act.run(g, what);
    if (r.ok) { setNote(t(what === "close" ? "closed" : "groupCancelled", { name: g.productName })); list.reload(); }
  }

  return (
    <Stack>
      {note && <Notice kind="success">{note}</Notice>}
      {act.error && <Notice kind="danger">{act.error.message}</Notice>}
      <Card title={t("groupHeading")}>
        <p>{t("groupIntro")}</p>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(g) => g.id}
            empty={t("groupEmpty")}
            columns={[
              { header: t("colProduct"), cell: (g) => <>{g.productName} {g.buyingGroupId && <Badge>{t("inviteOnly")}</Badge>}</> },
              { header: t("colBulk"), cell: (g) => formatRand(g.bulkPriceCents), align: "right" },
              { header: t("colProgress"), cell: (g) => t("progress", { total: g.totalCases, min: g.minTotalCases, percent: g.progressPercent, shops: g.shops ?? 0 }) },
              { header: t("colCloses"), cell: (g) => formatDateTime(g.closesAt) },
              { header: t("colStatus"), cell: (g) => <Badge>{g.status}</Badge> },
              { header: "", cell: (g) => g.status === "open" ? (
                <Inline>
                  <Button variant="secondary" disabled={act.busy} aria-label={t("close", { name: g.productName })} onClick={() => void run(g, "close")}>{t("closeShort")}</Button>
                  <Button variant="quiet" disabled={act.busy} aria-label={t("cancelGroup", { name: g.productName })} onClick={() => void run(g, "cancel")}>{t("cancelShort")}</Button>
                </Inline>
              ) : "" },
            ]}
          />
        )}
      </Card>
      <NewGroupDeal supplierId={supplierId} onPosted={(name) => { setNote(t("postedGroup", { name })); list.reload(); }} />
    </Stack>
  );
}
