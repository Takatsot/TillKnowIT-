import { formatDateTime, formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import type { GroupDeal } from "./types";

export function GroupDeals() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const list = useLoad(() => api.get<GroupDeal[]>(`/shops/${shopId}/group-deals`, { callerPersonId: personId }), [shopId, personId]);
  const [cases, setCases] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState("");

  const join = useAction((dealId: string, n: number) => api.post<GroupDeal>(`/shops/${shopId}/group-deals/${dealId}/commitments`, { callerPersonId: personId, cases: n }));

  async function submit(deal: GroupDeal) {
    setMessage("");
    const raw = (cases[deal.id] ?? "").trim();
    if (!/^\d+$/.test(raw) || Number(raw) < 1) { setErrors((e) => ({ ...e, [deal.id]: t("casesInvalid") })); return; }
    setErrors((e) => ({ ...e, [deal.id]: "" }));
    const r = await join.run(deal.id, Number(raw));
    if (r.ok) {
      setMessage(t("joined", { n: raw, name: deal.productName }));
      setCases((c) => ({ ...c, [deal.id]: "" }));
      list.reload();
    }
  }

  return (
    <Card title={t("groupsHeading")}>
      <Stack>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(d) => d.id}
            empty={t("groupsEmpty")}
            columns={[
              { header: t("colDeal"), cell: (d) => d.productName },
              { header: t("colSupplier"), cell: (d) => d.supplierName },
              { header: t("colGroupPrice"), align: "right", cell: (d) => formatRand(d.casePriceCents) },
              { header: t("colProgress"), cell: (d) => t("progress", { total: d.totalCases, min: d.minTotalCases, percent: d.progressPercent }) },
              { header: t("colCloses"), cell: (d) => formatDateTime(d.closesAt) },
              { header: t("colYours"), align: "right", cell: (d) => d.yourCases ?? "" },
              {
                header: t("colJoin"),
                cell: (d) => (
                  <Inline>
                    <TextField label={t("joinCases", { name: d.productName })} inputMode="numeric" value={cases[d.id] ?? ""} error={errors[d.id]} onChange={(e) => setCases((c) => ({ ...c, [d.id]: e.target.value }))} />
                    <Button disabled={join.busy} aria-label={t("joinFor", { name: d.productName })} onClick={() => void submit(d)}>{t("join")}</Button>
                  </Inline>
                ),
              },
            ]}
          />
        )}
        {join.error && <Notice kind="danger">{join.error.message}</Notice>}
        {message && <Notice kind="success">{message}</Notice>}
      </Stack>
    </Card>
  );
}
