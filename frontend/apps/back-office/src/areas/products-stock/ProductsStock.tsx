import { formatDate, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type ProductsStockKey } from "./messages";

export interface StockRow {
  productId: string;
  name: string;
  barcode: string | null;
  onHand: number;
  lowStockLevel: number;
  expiryDate: string | null;
  daysLeft: number | null;
  suggestedOrderQuantity: number;
  flags: string[];
}

export interface Lookup {
  code: string;
  names: Record<string, string>;
  isActive: boolean;
}

export const FILTERS = ["all", "low", "out", "not_selling", "expiring"] as const;
type Filter = (typeof FILTERS)[number];
const filterKey: Record<Filter, ProductsStockKey> = { all: "filterAll", low: "filterLow", out: "filterOut", not_selling: "filterNotSelling", expiring: "filterExpiring" };
const flagKeys: Record<string, ProductsStockKey> = {
  out: "flag_out", low: "flag_low", not_selling: "flag_not_selling", expiring: "flag_expiring", expired: "flag_expired", needs_count: "flag_needs_count",
};

interface Draft {
  id: string | null;
  name: string;
  barcode: string;
  category: string;
  cost: string;
  price: string;
  low: string;
  expiry: string;
  active: boolean;
}
const blank: Draft = { id: null, name: "", barcode: "", category: "", cost: "", price: "", low: "0", expiry: "", active: true };

type Errors = Partial<Record<"name" | "category" | "cost" | "price" | "low", string>>;

export function ProductsStock() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId, deviceId, language } = useSession();
  const [filter, setFilter] = useState<Filter>("all");
  const stock = useLoad(() => api.get<StockRow[]>(`/shops/${shopId}/stock`, { filter: filter === "all" ? undefined : filter }), [shopId, filter]);
  const categories = useLoad(() => api.get<Lookup[]>("/lookups/categories"), []);
  const [draft, setDraft] = useState<Draft>(blank);
  const [errors, setErrors] = useState<Errors>({});
  const [done, setDone] = useState("");

  const save = useAction((body: unknown, id: string | null) => (id ? api.patch<unknown>(`/products/${id}`, body) : api.post<unknown>("/products", body)));
  const set = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }));

  function validate() {
    const e: Errors = {};
    if (draft.name.trim() === "") e.name = t("nameRequired");
    if (draft.category === "") e.category = t("categoryRequired");
    const cost = parseRandToCents(draft.cost);
    const price = parseRandToCents(draft.price);
    if (cost === null) e.cost = t("costInvalid");
    if (price === null) e.price = t("priceInvalid");
    if (!/^\d+$/.test(draft.low.trim())) e.low = t("lowInvalid");
    setErrors(e);
    return Object.keys(e).length === 0 ? { cost: cost!, price: price! } : null;
  }

  async function submit() {
    setDone("");
    const parsed = validate();
    if (!parsed) return;
    const id = draft.id ?? crypto.randomUUID();
    const body = {
      shopId,
      deviceId,
      product: {
        id, staffId: personId, catalogueProductId: null, name: draft.name.trim(), barcode: draft.barcode.trim() === "" ? null : draft.barcode.trim(),
        categoryCode: draft.category, costCents: parsed.cost, priceCents: parsed.price, lowStockLevel: Number(draft.low),
        expiryDate: draft.expiry === "" ? null : draft.expiry, isActive: draft.active, updatedAt: new Date().toISOString(),
      },
    };
    const r = await save.run(body, draft.id);
    if (r.ok) {
      setDone(t(draft.id ? "updated" : "created", { name: draft.name.trim() }));
      setDraft(blank);
      stock.reload();
    }
  }

  const lookupName = (l: Lookup) => l.names[language] ?? l.names.en ?? l.code;

  return (
    <Stack>
      <Card title={t("heading")}>
        <Stack>
          <SelectField label={t("filter")} value={filter} onChange={(e) => setFilter(e.target.value as Filter)}>
            {FILTERS.map((f) => <option key={f} value={f}>{t(filterKey[f])}</option>)}
          </SelectField>
          {stock.error && <Notice kind="danger">{stock.error.message}</Notice>}
          {stock.loading && !stock.data ? <p>{t("loading")}</p> : (
            <Table
              rows={stock.data ?? []}
              rowKey={(r) => r.productId}
              empty={t("empty")}
              columns={[
                { header: t("colName"), cell: (r) => r.name },
                { header: t("colBarcode"), cell: (r) => r.barcode ?? "" },
                { header: t("colOnHand"), align: "right", cell: (r) => r.onHand },
                { header: t("colLow"), align: "right", cell: (r) => r.lowStockLevel },
                { header: t("colDaysLeft"), align: "right", cell: (r) => r.daysLeft ?? "" },
                { header: t("colExpiry"), cell: (r) => formatDate(r.expiryDate) },
                { header: t("colSuggested"), align: "right", cell: (r) => (r.suggestedOrderQuantity > 0 ? t("units", { n: r.suggestedOrderQuantity }) : "") },
                { header: t("colFlags"), cell: (r) => <Inline>{r.flags.map((f) => <Badge key={f}>{flagKeys[f] ? t(flagKeys[f]) : f}</Badge>)}</Inline> },
                {
                  header: t("colActions"),
                  cell: (r) => (
                    <Button variant="secondary" aria-label={t("edit", { name: r.name })} onClick={() => {
                      setDone("");
                      setErrors({});
                      setDraft({ ...blank, id: r.productId, name: r.name, barcode: r.barcode ?? "", low: String(r.lowStockLevel), expiry: r.expiryDate ?? "" });
                    }}>{t("editButton")}</Button>
                  ),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      <Card title={draft.id ? t("edit", { name: draft.name }) : t("add")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }} noValidate>
          <Stack>
            {draft.id && <Notice kind="info">{t("editHint")}</Notice>}
            <Inline>
              <TextField label={t("name")} value={draft.name} error={errors.name} onChange={(e) => set({ name: e.target.value })} required />
              <TextField label={t("barcode")} value={draft.barcode} onChange={(e) => set({ barcode: e.target.value })} />
              <SelectField label={t("category")} value={draft.category} error={errors.category} onChange={(e) => set({ category: e.target.value })}>
                <option value="">{t("categoryChoose")}</option>
                {(categories.data ?? []).filter((c) => c.isActive).map((c) => <option key={c.code} value={c.code}>{lookupName(c)}</option>)}
              </SelectField>
            </Inline>
            <Inline>
              <TextField label={t("cost")} inputMode="decimal" value={draft.cost} error={errors.cost} onChange={(e) => set({ cost: e.target.value })} />
              <TextField label={t("price")} inputMode="decimal" value={draft.price} error={errors.price} onChange={(e) => set({ price: e.target.value })} />
              <TextField label={t("lowLevel")} inputMode="numeric" value={draft.low} error={errors.low} onChange={(e) => set({ low: e.target.value })} />
              <TextField label={t("expiry")} type="date" value={draft.expiry} onChange={(e) => set({ expiry: e.target.value })} />
            </Inline>
            <CheckField label={t("active")} checked={draft.active} onChange={(e) => set({ active: e.target.checked })} />
            <p>{t("callerHint")}</p>
            {deviceId === "" && <Notice kind="warning">{t("needsDevice")}</Notice>}
            {save.error && <Notice kind="danger">{save.error.message}</Notice>}
            {done && <Notice kind="success">{done}</Notice>}
            <Inline>
              <Button type="submit" disabled={save.busy}>{t("save")}</Button>
              {draft.id && <Button variant="quiet" onClick={() => { setDraft(blank); setErrors({}); }}>{t("cancelEdit")}</Button>}
            </Inline>
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}
