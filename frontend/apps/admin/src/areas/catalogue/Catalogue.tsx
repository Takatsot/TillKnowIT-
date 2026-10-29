import { formatDate, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type CatalogueKey } from "./messages";

export interface Suggestion {
  id: string;
  barcode: string;
  name: string;
  size: string | null;
  categoryCode: string;
  suggestedAt: string;
}

export interface Lookup {
  type: string;
  code: string;
  names: Record<string, string>;
  isActive: boolean;
  sortOrder: number;
}

interface CatalogueProduct {
  id: string;
  barcode: string;
  name: string;
}

/** The lookup types the server accepts (Catalogue.Domain/Platform.cs). */
export const lookupTypes = ["categories", "units", "payment_methods", "vat_rates", "adjustment_reasons", "document_types", "languages", "municipalities"] as const;

const lookupName = (l: Lookup) => l.names.en ?? Object.values(l.names)[0] ?? l.code;

export function Catalogue() {
  return (
    <Stack>
      <Suggestions />
      <Lookups />
    </Stack>
  );
}

type Mode = { kind: "approve" | "reject" | "merge"; item: Suggestion };

function Suggestions() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Suggestion[]>("/admin/catalogue/suggestions", { adminPersonId: personId }), [personId]);
  const categories = useLoad(() => api.get<Lookup[]>("/lookups/categories"), []);
  const [mode, setMode] = useState<Mode | null>(null);
  const [note, setNote] = useState("");

  return (
    <>
      <Card title={t("suggestionsHeading")}>
        <p>{t("suggestionsIntro")}</p>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(s) => s.id}
            empty={t("suggestionsEmpty")}
            columns={[
              { header: t("colBarcode"), cell: (s) => s.barcode },
              { header: t("colName"), cell: (s) => s.name },
              { header: t("colSize"), cell: (s) => s.size ?? "" },
              { header: t("colCategory"), cell: (s) => <Badge>{s.categoryCode}</Badge> },
              { header: t("colSuggested"), cell: (s) => formatDate(s.suggestedAt) },
              {
                header: t("colActions"),
                cell: (s) => (
                  <Inline>
                    <Button variant="secondary" aria-label={`${t("approve")} ${s.name}`} onClick={() => { setNote(""); setMode({ kind: "approve", item: s }); }}>{t("approve")}</Button>
                    <Button variant="quiet" aria-label={`${t("merge")} ${s.name}`} onClick={() => { setNote(""); setMode({ kind: "merge", item: s }); }}>{t("merge")}</Button>
                    <Button variant="quiet" aria-label={`${t("reject")} ${s.name}`} onClick={() => { setNote(""); setMode({ kind: "reject", item: s }); }}>{t("reject")}</Button>
                  </Inline>
                ),
              },
            ]}
          />
        )}
      </Card>
      {mode && (
        <Review
          key={`${mode.kind}:${mode.item.id}`}
          mode={mode}
          categories={categories.data ?? []}
          onClose={() => setMode(null)}
          onDone={(message) => { setNote(message); setMode(null); list.reload(); }}
        />
      )}
    </>
  );
}

function Review({ mode, categories, onClose, onDone }: { mode: Mode; categories: Lookup[]; onClose: () => void; onDone: (message: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const { item } = mode;
  const base = `/admin/catalogue/suggestions/${item.id}`;
  const [name, setName] = useState("");
  const [size, setSize] = useState("");
  const [category, setCategory] = useState("");
  const [reason, setReason] = useState("");
  const [barcode, setBarcode] = useState("");
  const [found, setFound] = useState<CatalogueProduct | null>(null);

  const approve = useAction(() => api.post<void>(`${base}/approve`, {
    adminPersonId: personId, name: name.trim() === "" ? null : name.trim(), size: size.trim() === "" ? null : size.trim(), categoryCode: category === "" ? null : category,
  }));
  const reject = useAction(() => api.post<void>(`${base}/reject`, { adminPersonId: personId, reason: reason.trim() }));
  const find = useAction(() => api.get<CatalogueProduct>("/catalogue/products", { barcode: barcode.trim() }));
  const merge = useAction(() => api.post<void>(`${base}/merge`, { adminPersonId: personId, intoProductId: found!.id }));

  async function run(kind: "approved" | "rejected" | "merged", go: () => Promise<{ ok: boolean }>) {
    const r = await go();
    if (r.ok) onDone(t(kind, { name: item.name }));
  }

  async function lookUp() {
    setFound(null);
    const r = await find.run();
    if (r.ok) setFound(r.value);
  }

  return (
    <Card title={mode.kind === "approve" ? t("approving", { name: item.name }) : mode.kind === "reject" ? t("rejecting", { name: item.name }) : t("merging", { name: item.name })}>
      <Stack>
        {mode.kind === "approve" && (
          <form onSubmit={(e) => { e.preventDefault(); void run("approved", approve.run); }}>
            <Stack>
              <p>{t("approveHint")}</p>
              <Inline>
                <TextField label={t("fieldName")} placeholder={item.name} value={name} onChange={(e) => setName(e.target.value)} />
                <TextField label={t("fieldSize")} placeholder={item.size ?? ""} value={size} onChange={(e) => setSize(e.target.value)} />
                <SelectField label={t("fieldCategory")} value={category} onChange={(e) => setCategory(e.target.value)}>
                  <option value="">{t("keepCategory")} ({item.categoryCode})</option>
                  {categories.filter((c) => c.isActive).map((c) => <option key={c.code} value={c.code}>{lookupName(c)}</option>)}
                </SelectField>
              </Inline>
              {approve.error && <Notice kind="danger">{approve.error.message}</Notice>}
              <Button type="submit" disabled={approve.busy}>{t("confirmApprove")}</Button>
            </Stack>
          </form>
        )}
        {mode.kind === "reject" && (
          <form onSubmit={(e) => { e.preventDefault(); void run("rejected", reject.run); }}>
            <Stack>
              <TextField label={t("reason")} value={reason} onChange={(e) => setReason(e.target.value)} required />
              {reject.error && <Notice kind="danger">{reject.error.message}</Notice>}
              <Button type="submit" disabled={reject.busy || reason.trim() === ""}>{t("confirmReject")}</Button>
            </Stack>
          </form>
        )}
        {mode.kind === "merge" && (
          <Stack>
            <form onSubmit={(e) => { e.preventDefault(); void lookUp(); }}>
              <Inline>
                <TextField label={t("mergeBarcode")} inputMode="numeric" value={barcode} onChange={(e) => { setBarcode(e.target.value); setFound(null); }} required />
                <Button type="submit" variant="secondary" disabled={find.busy || barcode.trim() === ""}>{t("find")}</Button>
              </Inline>
            </form>
            {find.error && <Notice kind="danger">{find.error.message}</Notice>}
            {found && (
              <>
                <Notice kind="info">{t("foundProduct", { name: found.name, barcode: found.barcode })}</Notice>
                <Button disabled={merge.busy} onClick={() => void run("merged", merge.run)}>{t("confirmMerge")}</Button>
              </>
            )}
            {merge.error && <Notice kind="danger">{merge.error.message}</Notice>}
          </Stack>
        )}
        <Button variant="quiet" onClick={onClose}>{t("close")}</Button>
      </Stack>
    </Card>
  );
}

function Lookups() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [type, setType] = useState<string>(lookupTypes[0]);
  const list = useLoad(() => api.get<Lookup[]>(`/lookups/${type}`, { includeInactive: true }), [type]);
  const [edit, setEdit] = useState<{ isNew: boolean; code: string; english: string; sort: string; active: boolean } | null>(null);
  const [note, setNote] = useState("");
  const save = useAction(() => {
    const e = edit!;
    return api.put<Lookup>(`/admin/lookups/${type}/${e.code.trim()}`, {
      adminPersonId: personId, names: { en: e.english.trim() }, isActive: e.active, sortOrder: e.sort.trim() === "" ? null : Number(e.sort),
    });
  });

  async function submit() {
    const code = edit!.code.trim();
    const r = await save.run();
    if (r.ok) {
      setNote(t("lookupSaved", { code }));
      setEdit(null);
      list.reload();
    }
  }

  const change = (patch: Partial<NonNullable<typeof edit>>) => setEdit((e) => (e ? { ...e, ...patch } : e));

  return (
    <Card title={t("lookupsHeading")}>
      <Stack>
        <p>{t("lookupsIntro")}</p>
        <SelectField label={t("lookupType")} value={type} onChange={(e) => { setType(e.target.value); setEdit(null); setNote(""); }}>
          {lookupTypes.map((x) => <option key={x} value={x}>{t(`types_${x}` as CatalogueKey)}</option>)}
        </SelectField>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(l) => l.code}
            empty={t("lookupsEmpty")}
            columns={[
              { header: t("colCode"), cell: (l) => l.code },
              { header: t("colName"), cell: (l) => lookupName(l) },
              { header: t("colSort"), cell: (l) => l.sortOrder, align: "right" },
              { header: t("colStatus"), cell: (l) => <Badge>{l.isActive ? t("active") : t("inactive")}</Badge> },
              {
                header: t("colActions"),
                cell: (l) => <Button variant="secondary" aria-label={`${t("edit")} ${l.code}`} onClick={() => { setNote(""); setEdit({ isNew: false, code: l.code, english: l.names.en ?? "", sort: String(l.sortOrder), active: l.isActive }); }}>{t("edit")}</Button>,
              },
            ]}
          />
        )}
        {!edit && <Button onClick={() => { setNote(""); setEdit({ isNew: true, code: "", english: "", sort: "", active: true }); }}>{t("newLookup")}</Button>}
        {edit && (
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <h3>{edit.isNew ? t("newLookup") : t("editing", { code: edit.code })}</h3>
              <Inline>
                {edit.isNew && <TextField label={t("code")} hint={t("codeHint")} value={edit.code} onChange={(e) => change({ code: e.target.value })} required />}
                <TextField label={t("english")} value={edit.english} onChange={(e) => change({ english: e.target.value })} required />
                <TextField label={t("sortOrder")} inputMode="numeric" value={edit.sort} onChange={(e) => change({ sort: e.target.value })} />
              </Inline>
              <CheckField label={t("isActive")} checked={edit.active} onChange={(e) => change({ active: e.target.checked })} />
              {save.error && <Notice kind="danger">{save.error.message}</Notice>}
              <Inline>
                <Button type="submit" disabled={save.busy || edit.code.trim() === "" || edit.english.trim() === ""}>{t("saveLookup")}</Button>
                <Button variant="quiet" onClick={() => { save.clear(); setEdit(null); }}>{t("close")}</Button>
              </Inline>
            </Stack>
          </form>
        )}
      </Stack>
    </Card>
  );
}
