import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useEffect, useState } from "react";
import { messages } from "./messages";
import type { Break, Menu, MenuItem, School } from "./types";

export function MenuTab({ school }: { school: School }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const menu = useLoad(() => api.get<Menu>(`/schools/${school.id}/menu`, { callerPersonId: personId }), [school.id, personId]);

  return (
    <Stack>
      {menu.error && <Notice kind="danger">{menu.error.message}</Notice>}
      {menu.loading && !menu.data ? <p>{t("loading")}</p> : menu.data && (
        <>
          <Breaks school={school} initial={menu.data.breaks} />
          <Items school={school} items={menu.data.items} reload={menu.reload} />
        </>
      )}
    </Stack>
  );
}

function Breaks({ school, initial }: { school: School; initial: Break[] }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [rows, setRows] = useState<Break[]>(initial);
  const [done, setDone] = useState(false);
  const save = useAction(() => api.put<void>(`/schools/${school.id}/breaks`, { callerPersonId: personId, breaks: rows }));
  useEffect(() => setRows(initial), [initial]);

  const set = (i: number, patch: Partial<Break>) => { setDone(false); setRows(rows.map((r, n) => (n === i ? { ...r, ...patch } : r))); };

  async function submit() {
    setDone(false);
    const r = await save.run();
    if (r.ok) setDone(true);
  }

  return (
    <Card title={t("breaksHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <p>{t("breaksHint")}</p>
          {rows.map((b, i) => (
            <Inline key={i}>
              <TextField label={`${t("breakCode")} ${i + 1}`} value={b.code} onChange={(e) => set(i, { code: e.target.value })} />
              <TextField label={`${t("breakName")} ${i + 1}`} value={b.name} onChange={(e) => set(i, { name: e.target.value })} />
              <TextField label={`${t("breakCutOff")} ${i + 1}`} placeholder="09:30" value={b.cutOff} onChange={(e) => set(i, { cutOff: e.target.value })} />
              <Button variant="quiet" onClick={() => { setDone(false); setRows(rows.filter((_, n) => n !== i)); }}>{t("removeBreak", { n: i + 1 })}</Button>
            </Inline>
          ))}
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          {done && <Notice kind="success">{t("breaksSaved")}</Notice>}
          <Inline>
            <Button variant="secondary" onClick={() => { setDone(false); setRows([...rows, { code: "", name: "", cutOff: "" }]); }}>{t("addBreak")}</Button>
            <Button type="submit" disabled={save.busy}>{t("saveBreaks")}</Button>
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}

interface Draft {
  id: string | null;
  name: string;
  price: string;
  productId: string;
  categoryCode: string;
  available: boolean;
}

const blank: Draft = { id: null, name: "", price: "", productId: "", categoryCode: "", available: true };

function Items({ school, items, reload }: { school: School; items: MenuItem[]; reload: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [priceError, setPriceError] = useState<string | null>(null);
  const [done, setDone] = useState("");
  const save = useAction((body: unknown) => (draft!.id === null ? api.post<MenuItem>(`/schools/${school.id}/menu`, body) : api.put<MenuItem>(`/menu-items/${draft!.id}`, body)));

  async function submit() {
    const d = draft!;
    const cents = parseRandToCents(d.price);
    setPriceError(cents === null ? t("itemPriceInvalid") : null);
    if (cents === null) return;
    const r = await save.run({
      callerPersonId: personId, name: d.name.trim(), priceCents: cents, productId: d.productId.trim() === "" ? null : d.productId.trim(),
      categoryCode: d.categoryCode.trim() === "" ? null : d.categoryCode.trim(), isAvailable: d.available,
    });
    if (r.ok) {
      setDone(t("itemSaved", { name: r.value.name }));
      setDraft(null);
      reload();
    }
  }

  const change = (patch: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...patch } : d));

  return (
    <Card title={t("menuHeading")}>
      <Stack>
        {done && <Notice kind="success">{done}</Notice>}
        <Table
          rows={items}
          rowKey={(i) => i.id}
          empty={t("menuEmpty")}
          columns={[
            { header: t("colItem"), cell: (i) => i.name },
            { header: t("colPrice"), cell: (i) => formatRand(i.priceCents), align: "right" },
            { header: t("colAvailable"), cell: (i) => <Badge>{i.isAvailable ? t("yes") : t("no")}</Badge> },
            {
              header: "",
              cell: (i) => <Button variant="secondary" aria-label={`${t("edit")} ${i.name}`} onClick={() => { setDone(""); setPriceError(null); setDraft({ id: i.id, name: i.name, price: String(i.priceCents / 100), productId: i.productId ?? "", categoryCode: i.categoryCode ?? "", available: i.isAvailable }); }}>{t("edit")}</Button>,
            },
          ]}
        />
        {!draft && <Button onClick={() => { setDone(""); setPriceError(null); setDraft(blank); }}>{t("newItem")}</Button>}
        {draft && (
          <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
            <Stack>
              <h3>{draft.id === null ? t("newItem") : t("editingItem", { name: draft.name })}</h3>
              <Inline>
                <TextField label={t("itemName")} value={draft.name} onChange={(e) => change({ name: e.target.value })} required />
                <TextField label={t("itemPrice")} inputMode="decimal" value={draft.price} error={priceError} onChange={(e) => change({ price: e.target.value })} required />
              </Inline>
              <Inline>
                <TextField label={t("productId")} value={draft.productId} onChange={(e) => change({ productId: e.target.value })} />
                <TextField label={t("categoryCode")} value={draft.categoryCode} onChange={(e) => change({ categoryCode: e.target.value })} />
              </Inline>
              <CheckField label={t("itemAvailable")} checked={draft.available} onChange={(e) => change({ available: e.target.checked })} />
              {save.error && <Notice kind="danger">{save.error.message}</Notice>}
              <Inline>
                <Button type="submit" disabled={save.busy || draft.name.trim() === ""}>{t("saveItem")}</Button>
                <Button variant="quiet" onClick={() => { save.clear(); setDraft(null); }}>{t("cancel")}</Button>
              </Inline>
            </Stack>
          </form>
        )}
      </Stack>
    </Card>
  );
}
