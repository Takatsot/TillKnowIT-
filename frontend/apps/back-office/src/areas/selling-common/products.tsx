import { useApi, useLoad, useSession } from "@tillknowit/app-kit";
import { CheckField, SelectField } from "@tillknowit/design-system";
import { useMessages } from "@tillknowit/app-kit";
import { messages } from "./messages";

/** A product as the shop's product list returns it (GET /shops/{id}/products, PulledProduct, including inactive ones); the screens use the ones they need. */
export interface ProductRef {
  productId: string;
  name: string;
  barcode?: string | null;
  categoryCode?: string;
  costCents?: number;
  priceCents?: number;
  isActive?: boolean;
}

interface PulledProduct { id: string; name: string; barcode: string | null; categoryCode: string; costCents: number; priceCents: number; isActive: boolean }

/** The shop's products with prices (GET /shops/{id}/products). Includes inactive products. */
export function useProducts() {
  const api = useApi();
  const { shopId, personId } = useSession();
  return useLoad(async () => {
    const r = await api.get<PulledProduct[]>(`/shops/${shopId}/products`, { callerPersonId: personId });
    if (!r.ok) return r;
    return { ok: true as const, value: r.value.map((p): ProductRef => ({ productId: p.id, name: p.name, barcode: p.barcode, categoryCode: p.categoryCode, costCents: p.costCents, priceCents: p.priceCents, isActive: p.isActive })) };
  }, [shopId, personId]);
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
