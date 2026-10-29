import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { ProductChecks, ProductSelect, useProducts, type ProductRef } from "../selling-common/products";
import { useTillCaller, type TillCaller } from "../selling-common/TillCaller";
import { messages } from "./messages";

interface ModifierGroup { id: string; name: string; required: boolean; min: number; max: number; options: { productId: string; name: string; priceCents: number }[] }
interface Variant { id: string; parentProductId: string; name: string; label: string; barcode: string | null; costCents: number; priceCents: number }
interface FloorTable { id?: string; area: string; name: string; seats: number }

type Tab = "modifiers" | "variants" | "floor";

export function ModifiersAndFloor() {
  const t = useMessages(messages);
  const caller = useTillCaller();
  const products = useProducts();
  const [tab, setTab] = useState<Tab>("modifiers");
  const tabs: [Tab, string][] = [["modifiers", t("tabModifiers")], ["variants", t("tabVariants")], ["floor", t("tabFloor")]];
  return (
    <Stack>
      {caller.card}
      <Inline>
        {tabs.map(([id, label]) => <Button key={id} variant={tab === id ? "primary" : "secondary"} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}
      </Inline>
      {tab === "modifiers" && <Modifiers caller={caller} products={products.data ?? []} />}
      {tab === "variants" && <Variants products={products.data ?? []} />}
      {tab === "floor" && <Floor caller={caller} />}
    </Stack>
  );
}

// ------------------------------------------------------------------ modifier groups

function Modifiers({ caller, products }: { caller: TillCaller; products: readonly ProductRef[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const groups = useLoad(
    () => (caller.ready ? api.get<ModifierGroup[]>(`/shops/${shopId}/modifier-groups`, { deviceId: caller.deviceId, staffId: caller.staffId }) : Promise.resolve({ ok: true as const, value: [] as ModifierGroup[] })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const [editing, setEditing] = useState<ModifierGroup | null>(null);
  const [note, setNote] = useState("");

  return (
    <Stack>
      <Card title={t("groupsHeading")}>
        <Stack>
          <p>{t("groupsHint")}</p>
          {groups.error && <Notice kind="danger">{groups.error.message}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {groups.loading && !groups.data ? <p>{t("loading")}</p> : (
            <Table
              rows={groups.data ?? []}
              rowKey={(g) => g.id}
              empty={t("groupsEmpty")}
              columns={[
                { header: t("colGroup"), cell: (g) => g.name },
                { header: t("colRule"), cell: (g) => (g.required ? t("requiredRule", { min: g.min, max: g.max }) : t("optionalRule", { max: g.max })) },
                { header: t("colOptions"), cell: (g) => g.options.map((o) => `${o.name} (${formatRand(o.priceCents)})`).join(", ") },
                { header: t("colActions"), cell: (g) => <Button variant="secondary" onClick={() => { setNote(""); setEditing(g); }}>{t("editGroup", { name: g.name })}</Button> },
              ]}
            />
          )}
        </Stack>
      </Card>
      <GroupForm
        key={editing?.id ?? "new"}
        editing={editing}
        products={products}
        onSaved={(g) => { setNote(t("groupSaved", { name: g.name })); setEditing(null); groups.reload(); }}
        onCancel={() => setEditing(null)}
      />
      <AttachGroups caller={caller} products={products} groups={groups.data ?? []} />
    </Stack>
  );
}

function GroupForm({ editing, products, onSaved, onCancel }: { editing: ModifierGroup | null; products: readonly ProductRef[]; onSaved: (g: ModifierGroup) => void; onCancel: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const [name, setName] = useState(editing?.name ?? "");
  const [required, setRequired] = useState(editing?.required ?? false);
  const [min, setMin] = useState(String(editing?.min ?? 0));
  const [max, setMax] = useState(String(editing?.max ?? 1));
  const [options, setOptions] = useState<string[]>(editing?.options.map((o) => o.productId) ?? []);
  const save = useAction((body: unknown) => (editing ? api.put<ModifierGroup>(`/shops/${shopId}/modifier-groups/${editing.id}`, body) : api.post<ModifierGroup>(`/shops/${shopId}/modifier-groups`, body)));

  async function submit() {
    const r = await save.run({ callerPersonId: personId, name: name.trim(), required, min: Number(min) || 0, max: Number(max) || 0, optionProductIds: options });
    if (r.ok) { if (!editing) { setName(""); setRequired(false); setMin("0"); setMax("1"); setOptions([]); } onSaved(r.value); }
  }

  return (
    <Card title={editing ? t("formEdit") : t("formNew")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("groupName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("min")} type="number" min={0} value={min} onChange={(e) => setMin(e.target.value)} />
            <TextField label={t("max")} type="number" min={1} value={max} onChange={(e) => setMax(e.target.value)} />
            <CheckField label={t("required")} checked={required} onChange={(e) => setRequired(e.target.checked)} />
          </Inline>
          <ProductChecks legend={t("optionsLegend")} selected={options} onChange={setOptions} products={products} />
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          <Inline>
            <Button type="submit" disabled={save.busy || name.trim() === ""}>{t("saveGroup")}</Button>
            {editing && <Button variant="quiet" onClick={onCancel}>{t("stopEditing")}</Button>}
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}

function AttachGroups({ caller, products, groups }: { caller: TillCaller; products: readonly ProductRef[]; groups: readonly ModifierGroup[] }) {
  const t = useMessages(messages);
  const [productId, setProductId] = useState("");
  return (
    <Card title={t("attachHeading")}>
      <Stack>
        <ProductSelect label={t("attachProduct")} value={productId} onChange={setProductId} products={products} />
        {productId !== "" && <AttachForm key={productId} caller={caller} productId={productId} groups={groups} />}
      </Stack>
    </Card>
  );
}

function AttachForm({ caller, productId, groups }: { caller: TillCaller; productId: string; groups: readonly ModifierGroup[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const current = useLoad(() => api.get<ModifierGroup[]>(`/shops/${shopId}/products/${productId}/modifier-groups`, { deviceId: caller.deviceId, staffId: caller.staffId }), [shopId, productId, caller.deviceId, caller.staffId]);
  const [chosen, setChosen] = useState<string[] | null>(null);
  const [note, setNote] = useState("");
  const save = useAction((ids: string[]) => api.put<void>(`/shops/${shopId}/products/${productId}/modifier-groups`, { callerPersonId: personId, groupIds: ids }));
  const selected = chosen ?? (current.data ?? []).map((g) => g.id);

  if (current.loading && !current.data) return <p>{t("loading")}</p>;
  return (
    <Stack>
      {current.error && <Notice kind="danger">{current.error.message}</Notice>}
      {groups.length === 0 ? <p>{t("attachNone")}</p> : (
        <fieldset>
          <legend>{t("attachLegend")}</legend>
          {groups.map((g) => (
            <CheckField key={g.id} label={g.name} checked={selected.includes(g.id)} onChange={(e) => { setNote(""); setChosen(e.target.checked ? [...selected, g.id] : selected.filter((x) => x !== g.id)); }} />
          ))}
        </fieldset>
      )}
      {save.error && <Notice kind="danger">{save.error.message}</Notice>}
      {note && <Notice kind="success">{note}</Notice>}
      <div>
        <Button disabled={save.busy} onClick={() => void save.run(selected).then((r) => { if (r.ok) setNote(t("attachSaved")); })}>{t("attachSave")}</Button>
      </div>
    </Stack>
  );
}

// ------------------------------------------------------------------ variants

function Variants({ products }: { products: readonly ProductRef[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const [productId, setProductId] = useState("");
  const [label, setLabel] = useState("");
  const [barcode, setBarcode] = useState("");
  const [cost, setCost] = useState("");
  const [price, setPrice] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const list = useLoad(
    () => (productId === "" ? Promise.resolve({ ok: true as const, value: [] as Variant[] }) : api.get<Variant[]>(`/shops/${shopId}/products/${productId}/variants`, { callerPersonId: personId })),
    [shopId, personId, productId],
  );
  const add = useAction((body: unknown) => api.post<Variant[]>(`/shops/${shopId}/products/${productId}/variants`, body));

  async function submit() {
    const costCents = parseRandToCents(cost);
    const priceCents = parseRandToCents(price);
    if (costCents === null || priceCents === null) { setProblem(t("variantMoneyInvalid")); return; }
    setProblem(null);
    const r = await add.run({ callerPersonId: personId, variants: [{ label: label.trim(), barcode: barcode.trim() === "" ? null : barcode.trim(), costCents, priceCents }] });
    if (r.ok) { setNote(t("variantAdded", { label: label.trim() })); setLabel(""); setBarcode(""); setCost(""); setPrice(""); list.reload(); }
  }

  return (
    <Card title={t("variantsHeading")}>
      <Stack>
        <p>{t("variantsHint")}</p>
        <ProductSelect label={t("variantProduct")} value={productId} onChange={(id) => { setNote(""); setProductId(id); }} products={products} />
        {productId === "" ? <p>{t("chooseProductFirst")}</p> : (
          <>
            {list.error && <Notice kind="danger">{list.error.message}</Notice>}
            {list.loading && !list.data ? <p>{t("loading")}</p> : (
              <Table
                rows={list.data ?? []}
                rowKey={(v) => v.id}
                empty={t("variantsEmpty")}
                columns={[
                  { header: t("colLabel"), cell: (v) => v.label },
                  { header: t("colBarcode"), cell: (v) => v.barcode ?? "" },
                  { header: t("colCost"), cell: (v) => formatRand(v.costCents), align: "right" },
                  { header: t("colPrice"), cell: (v) => formatRand(v.priceCents), align: "right" },
                ]}
              />
            )}
            <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
              <Stack>
                <Inline>
                  <TextField label={t("variantLabel")} value={label} onChange={(e) => setLabel(e.target.value)} required />
                  <TextField label={t("variantBarcode")} value={barcode} onChange={(e) => setBarcode(e.target.value)} />
                  <TextField label={t("variantCost")} inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} required />
                  <TextField label={t("variantPrice")} inputMode="decimal" value={price} error={problem} onChange={(e) => setPrice(e.target.value)} required />
                  <Button type="submit" disabled={add.busy || label.trim() === ""}>{t("addVariant")}</Button>
                </Inline>
                {add.error && <Notice kind="danger">{add.error.message}</Notice>}
                {note && <Notice kind="success">{note}</Notice>}
              </Stack>
            </form>
          </>
        )}
      </Stack>
    </Card>
  );
}

// ------------------------------------------------------------------ floor plan

function Floor({ caller }: { caller: TillCaller }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const plan = useLoad(
    () => (caller.ready ? api.get<FloorTable[]>(`/shops/${shopId}/floor-plan`, { deviceId: caller.deviceId, staffId: caller.staffId }) : Promise.resolve({ ok: true as const, value: [] as FloorTable[] })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  return (
    <Card title={t("floorHeading")}>
      <Stack>
        <p>{t("floorHint")}</p>
        {plan.error && <Notice kind="danger">{plan.error.message}</Notice>}
        {plan.loading && !plan.data ? <p>{t("loading")}</p> : <FloorEditor key={JSON.stringify(plan.data)} initial={plan.data ?? []} />}
      </Stack>
    </Card>
  );
}

function FloorEditor({ initial }: { initial: FloorTable[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId, personId } = useSession();
  const [tables, setTables] = useState<{ area: string; name: string; seats: string }[]>(initial.map((x) => ({ area: x.area, name: x.name, seats: String(x.seats) })));
  const [problem, setProblem] = useState("");
  const [note, setNote] = useState("");
  const save = useAction((body: unknown) => api.put<FloorTable[]>(`/shops/${shopId}/floor-plan`, body));
  const edit = (n: number, patch: Partial<(typeof tables)[number]>) => { setNote(""); setTables((ts) => ts.map((x, i) => (i === n ? { ...x, ...patch } : x))); };

  async function submit() {
    if (tables.some((x) => x.name.trim() === "" || !(Number(x.seats) >= 1))) { setProblem(t("tableInvalid")); return; }
    setProblem("");
    const r = await save.run({ callerPersonId: personId, tables: tables.map((x) => ({ area: x.area.trim(), name: x.name.trim(), seats: Number(x.seats) })) });
    if (r.ok) setNote(t("floorSaved", { count: r.value.length }));
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <Stack>
        {tables.length === 0 && <p>{t("floorEmpty")}</p>}
        {tables.map((x, n) => (
          <Inline key={n}>
            <TextField label={`${t("tableArea")} ${n + 1}`} value={x.area} onChange={(e) => edit(n, { area: e.target.value })} />
            <TextField label={`${t("tableName")} ${n + 1}`} value={x.name} onChange={(e) => edit(n, { name: e.target.value })} />
            <TextField label={`${t("tableSeats")} ${n + 1}`} type="number" min={1} value={x.seats} onChange={(e) => edit(n, { seats: e.target.value })} />
            <Button variant="quiet" onClick={() => { setNote(""); setTables((ts) => ts.filter((_, i) => i !== n)); }}>{t("removeTable", { name: x.name || String(n + 1) })}</Button>
          </Inline>
        ))}
        {problem && <Notice kind="warning">{problem}</Notice>}
        {save.error && <Notice kind="danger">{save.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        <Inline>
          <Button variant="secondary" onClick={() => setTables((ts) => [...ts, { area: ts[ts.length - 1]?.area ?? "", name: "", seats: "4" }])}>{t("addTable")}</Button>
          <Button type="submit" disabled={save.busy}>{t("saveFloor")}</Button>
        </Inline>
      </Stack>
    </form>
  );
}
