import { useApi, useLoad, useSession } from "@tillknowit/app-kit";
import { CheckField, SelectField } from "@tillknowit/design-system";
import { useMessages } from "@tillknowit/app-kit";
import { messages } from "./messages";

/** A product as far as these screens need it: GET /shops/{id}/stock returns more (stock levels); we keep what we use. */
export interface ProductRef {
  productId: string;
  name: string;
}

/** The shop's products by name. There is no plain product list route; the stock list is the one that carries ids and names without a till. */
export function useProducts() {
  const api = useApi();
  const { shopId } = useSession();
  return useLoad(() => api.get<ProductRef[]>(`/shops/${shopId}/stock`), [shopId]);
}

export function ProductSelect({ label, value, onChange, products }: { label: string; value: string; onChange: (id: string) => void; products: readonly ProductRef[] }) {
  const t = useMessages(messages);
  return (
    <SelectField label={label} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{t("chooseProduct")}</option>
      {products.map((p) => <option key={p.productId} value={p.productId}>{p.name}</option>)}
    </SelectField>
  );
}

export function ProductChecks({ legend, selected, onChange, products }: { legend: string; selected: readonly string[]; onChange: (ids: string[]) => void; products: readonly ProductRef[] }) {
  const t = useMessages(messages);
  return (
    <fieldset>
      <legend>{legend}</legend>
      {products.length === 0 ? <p>{t("productsEmpty")}</p> : products.map((p) => (
        <CheckField key={p.productId} label={p.name} checked={selected.includes(p.productId)}
          onChange={(e) => onChange(e.target.checked ? [...selected, p.productId] : selected.filter((x) => x !== p.productId))} />
      ))}
    </fieldset>
  );
}
