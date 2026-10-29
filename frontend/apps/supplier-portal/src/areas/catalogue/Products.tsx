import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

export interface SupplierProduct {
  id: string; barcode: string | null; name: string; unitsPerCase: number; casePriceCents: number; unitPriceCents: number; vatIncluded: boolean; inStock: boolean;
  expiryPolicy: string | null; catalogueProductId: string | null;
}

function EditProduct({ product, onDone }: { product: SupplierProduct; onDone: (name: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [name, setName] = useState(product.name);
  const [units, setUnits] = useState(String(product.unitsPerCase));
  const [price, setPrice] = useState((product.casePriceCents / 100).toFixed(2));
  const [vat, setVat] = useState(product.vatIncluded);
  const [stock, setStock] = useState(product.inStock);
  const [expiry, setExpiry] = useState(product.expiryPolicy ?? "");
  const [problem, setProblem] = useState("");
  const save = useAction((body: object) => api.patch<SupplierProduct>(`/supplier-products/${product.id}`, body));

  async function submit() {
    const cents = parseRandToCents(price);
    const unitsPerCase = Number(units);
    if (cents === null) { setProblem(t("badPrice")); return; }
    if (!Number.isInteger(unitsPerCase) || unitsPerCase < 1) { setProblem(t("badUnits")); return; }
    setProblem("");
    const r = await save.run({ callerPersonId: personId, name: name.trim(), unitsPerCase, casePriceCents: cents, vatIncluded: vat, inStock: stock, expiryPolicy: expiry.trim() === "" ? null : expiry.trim() });
    if (r.ok) onDone(r.value.name);
  }

  return (
    <Card title={t("editHeading", { name: product.name })}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("fName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("fUnitsPerCase")} inputMode="numeric" value={units} onChange={(e) => setUnits(e.target.value)} />
            <TextField label={t("fCasePrice")} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
            <TextField label={t("fExpiry")} value={expiry} onChange={(e) => setExpiry(e.target.value)} />
          </Inline>
          <Inline>
            <CheckField label={t("fVat")} checked={vat} onChange={(e) => setVat(e.target.checked)} />
            <CheckField label={t("fInStock")} checked={stock} onChange={(e) => setStock(e.target.checked)} />
          </Inline>
          <Inline>
            <Button type="submit" disabled={save.busy || name.trim() === ""}>{t("save")}</Button>
            <Button variant="quiet" onClick={() => onDone("")}>{t("cancel")}</Button>
          </Inline>
          {problem && <Notice kind="danger">{problem}</Notice>}
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

export function Products({ supplierId }: { supplierId: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<SupplierProduct[]>(`/suppliers/${supplierId}/products`, { callerPersonId: personId }), [supplierId, personId]);
  const [editing, setEditing] = useState<SupplierProduct | null>(null);
  const [note, setNote] = useState("");

  return (
    <Stack>
      {note && <Notice kind="success">{note}</Notice>}
      {editing && <EditProduct key={editing.id} product={editing} onDone={(name) => { setEditing(null); if (name) { setNote(t("updated", { name })); list.reload(); } }} />}
      <Card title={t("productsHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(p) => p.id}
            empty={t("productsEmpty")}
            columns={[
              { header: t("colName"), cell: (p) => p.name },
              { header: t("colBarcode"), cell: (p) => p.barcode ?? "" },
              { header: t("colCase"), cell: (p) => p.unitsPerCase, align: "right" },
              { header: t("colCasePrice"), cell: (p) => formatRand(p.casePriceCents), align: "right" },
              { header: t("colUnitPrice"), cell: (p) => formatRand(p.unitPriceCents), align: "right" },
              { header: t("colStock"), cell: (p) => <Badge>{p.inStock ? t("inStock") : t("outOfStock")}</Badge> },
              { header: "", cell: (p) => <Button variant="secondary" aria-label={t("edit", { name: p.name })} onClick={() => { setNote(""); setEditing(p); }}>{t("editShort")}</Button> },
            ]}
          />
        )}
      </Card>
    </Stack>
  );
}
