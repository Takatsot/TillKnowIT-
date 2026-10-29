import { formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import type { BuiltOrder, Offer, OrderListItem, Offers } from "./types";

interface Selection {
  supplierId: string;
  supplierProductId: string;
  cases: number;
}

export function OrderList({ onBuilt }: { onBuilt?: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const list = useLoad(() => api.get<OrderListItem[]>(`/shops/${shopId}/order-list`, { callerPersonId: personId }), [shopId, personId]);
  const [picked, setPicked] = useState<Record<string, Selection>>({});
  const [offersFor, setOffersFor] = useState<string | null>(null);
  const offers = useLoad(
    () => (offersFor ? api.get<Offers>(`/shops/${shopId}/offers`, { product: offersFor, callerPersonId: personId }) : Promise.resolve({ ok: true as const, value: null as Offers | null })),
    [shopId, personId, offersFor],
  );
  const [method, setMethod] = useState("on_delivery");
  const [message, setMessage] = useState("");

  const build = useAction(() => api.post<BuiltOrder[]>(`/shops/${shopId}/purchase-orders/build`, { callerPersonId: personId, paymentMethod: method, selections: Object.values(picked) }));
  const usual = useAction((supplierId: string) => api.put<unknown>(`/shops/${shopId}/usual-suppliers`, { callerPersonId: personId, supplierId }));

  const toSelection = (o: Offer): Selection => ({ supplierId: o.supplierId, supplierProductId: o.supplierProductId, cases: o.cases });

  function toggle(item: OrderListItem, on: boolean) {
    setPicked((p) => {
      const next = { ...p };
      if (on && item.best) next[item.productId] = toSelection(item.best);
      else delete next[item.productId];
      return next;
    });
  }

  async function submit() {
    setMessage("");
    const r = await build.run();
    if (r.ok) {
      const warnings = r.value.map((b) => b.warning).filter((w): w is string => Boolean(w));
      setMessage([t("built", { n: r.value.length }), ...warnings.map((w) => t("buildWarning", { warning: w }))].join(" "));
      setPicked({});
      list.reload();
      onBuilt?.();
    }
  }

  async function makeUsual(o: Offer) {
    setMessage("");
    const r = await usual.run(o.supplierId);
    if (r.ok) {
      setMessage(t("usualSaved", { supplier: o.supplierName }));
      if (offersFor) offers.reload();
    }
  }

  const count = Object.keys(picked).length;
  const best = (o: Offer | null) => (o ? t("offerSummary", { supplier: o.supplierName, cases: o.cases, total: formatRand(o.totalCostCents) }) : t("noOffers"));

  return (
    <Stack>
      <Card title={t("listHeading")}>
        <Stack>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(i) => i.productId}
              empty={t("listEmpty")}
              columns={[
                { header: "", cell: (i) => <CheckField label={t("select", { name: i.productName })} checked={picked[i.productId] !== undefined} disabled={!i.best} onChange={(e) => toggle(i, e.target.checked)} /> },
                { header: t("colProduct"), cell: (i) => i.productName },
                { header: t("colOnHand"), align: "right", cell: (i) => i.onHand },
                { header: t("colDaysLeft"), align: "right", cell: (i) => i.daysLeft ?? "" },
                { header: t("colQuantity"), align: "right", cell: (i) => t("units", { n: i.quantityUnits }) },
                { header: t("colBest"), cell: (i) => best(i.best) },
                { header: t("colActions"), cell: (i) => <Button variant="secondary" aria-label={t("seeOffersFor", { name: i.productName })} onClick={() => setOffersFor(i.productId)}>{t("seeOffers")}</Button> },
              ]}
            />
          )}
          <Inline>
            <SelectField label={t("paymentMethod")} value={method} onChange={(e) => setMethod(e.target.value)}>
              <option value="on_delivery">{t("payOnDelivery")}</option>
              <option value="in_app">{t("payInApp")}</option>
            </SelectField>
            <Button disabled={build.busy || count === 0} onClick={() => void submit()}>{t("build")}</Button>
          </Inline>
          {count === 0 && <p>{t("buildNeeds")}</p>}
          {build.error && <Notice kind="danger">{build.error.message}</Notice>}
          {message && <Notice kind="success">{message}</Notice>}
        </Stack>
      </Card>
      {offersFor && (
        <Card title={t("offersHeading", { name: offers.data?.productName ?? "" })}>
          <Stack>
            {offers.error && <Notice kind="danger">{offers.error.message}</Notice>}
            {usual.error && <Notice kind="danger">{usual.error.message}</Notice>}
            {offers.loading && !offers.data ? <p>{t("loading")}</p> : (
              <Table
                rows={offers.data?.offers ?? []}
                rowKey={(o) => o.supplierProductId}
                empty={t("offersEmpty")}
                columns={[
                  { header: t("offerSupplier"), cell: (o) => o.supplierName },
                  { header: t("offerCases"), align: "right", cell: (o) => o.cases },
                  { header: t("offerUnits"), align: "right", cell: (o) => o.unitsReceived },
                  { header: t("offerTotal"), align: "right", cell: (o) => formatRand(o.totalCostCents) },
                  { header: t("offerPerUnit"), align: "right", cell: (o) => formatRand(o.trueCostPerUnitCents) },
                  { header: t("offerMargin"), align: "right", cell: (o) => (o.marginPercent === null ? "" : `${Math.round(o.marginPercent)}%`) },
                  { header: t("offerDelivery"), cell: (o) => t("offerDays", { n: o.deliveryDaysAway }) },
                  { header: t("offerRating"), cell: (o) => (o.rating === null ? "" : o.rating.toFixed(1)) },
                  {
                    header: t("offerNotes"),
                    cell: (o) => (
                      <Inline>
                        {o.isCheapest && <Badge>{t("cheapest")}</Badge>}
                        {o.isUsual && <Badge>{t("usual")}</Badge>}
                        {o.label && <Badge>{o.label === "Sponsored" ? t("sponsored") : o.label}</Badge>}
                        {o.dealTitle && <Badge>{o.dealTitle}</Badge>}
                        {!o.meetsMinimum && <Badge>{t("belowMinimum")}</Badge>}
                      </Inline>
                    ),
                  },
                  {
                    header: t("colActions"),
                    cell: (o) => (
                      <Inline>
                        <Button
                          variant={picked[offersFor]?.supplierProductId === o.supplierProductId ? "primary" : "secondary"}
                          aria-label={t("chooseFor", { supplier: o.supplierName, name: offers.data?.productName ?? "" })}
                          onClick={() => setPicked((p) => ({ ...p, [offersFor]: toSelection(o) }))}
                        >{picked[offersFor]?.supplierProductId === o.supplierProductId ? t("chosen") : t("choose")}</Button>
                        {!o.isUsual && <Button variant="quiet" disabled={usual.busy} aria-label={t("makeUsualFor", { supplier: o.supplierName })} onClick={() => void makeUsual(o)}>{t("makeUsual")}</Button>}
                      </Inline>
                    ),
                  },
                ]}
              />
            )}
          </Stack>
        </Card>
      )}
    </Stack>
  );
}
