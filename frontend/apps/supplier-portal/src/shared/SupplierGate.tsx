import { useMessages } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, Stack, TextField } from "@tillknowit/design-system";
import type { AreaMessages } from "@tillknowit/i18n";
import { useState, type ReactNode } from "react";

const en = {
  heading: "Which supplier?",
  explain: "Most supplier routes use the supplier id, which is not the same as the organisation id. The server has no route that finds it from your organisation, so type it in once (the application screen fills it in for you after you apply).",
  label: "Supplier id",
  save: "Use this supplier",
  invalid: "That does not look like an id. It is made of letters, numbers and dashes, for example 3f2b6c1e-0000-4000-8000-000000000000.",
  using: "Working as supplier {id}.",
  change: "Change supplier",
} as const;

type Key = keyof typeof en;
const messages: AreaMessages<Key> = { en };

const KEY = "tillknowit.supplierId";
const looksLikeId = (v: string) => /^[0-9a-fA-F-]{32,36}$/.test(v);

export function readSupplierId(): string {
  try { return localStorage.getItem(KEY) ?? ""; } catch { return ""; }
}

export function saveSupplierId(id: string): void {
  try { localStorage.setItem(KEY, id); } catch { /* storage unavailable: it lasts until the page closes */ }
}

/** Shows its children once a supplier id is known; until then asks for it. The id is remembered in this browser only. */
export function SupplierGate({ children }: { children: (supplierId: string) => ReactNode }) {
  const t = useMessages(messages);
  const [id, setId] = useState(readSupplierId);
  const [draft, setDraft] = useState("");
  const [invalid, setInvalid] = useState(false);

  if (id === "") {
    return (
      <Card title={t("heading")}>
        <Stack>
          <p>{t("explain")}</p>
          <form onSubmit={(e) => {
            e.preventDefault();
            const v = draft.trim();
            if (!looksLikeId(v)) { setInvalid(true); return; }
            setInvalid(false);
            saveSupplierId(v);
            setId(v);
          }}>
            <Inline>
              <TextField label={t("label")} value={draft} onChange={(e) => setDraft(e.target.value)} required />
              <Button type="submit">{t("save")}</Button>
            </Inline>
          </form>
          {invalid && <Notice kind="danger">{t("invalid")}</Notice>}
        </Stack>
      </Card>
    );
  }

  return (
    <Stack>
      <Inline>
        <span>{t("using", { id })}</span>
        <Button variant="quiet" onClick={() => { saveSupplierId(""); setId(""); }}>{t("change")}</Button>
      </Inline>
      {children(id)}
    </Stack>
  );
}
