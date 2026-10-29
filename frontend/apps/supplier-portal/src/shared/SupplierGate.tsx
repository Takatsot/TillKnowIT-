import { useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, Stack, TextField } from "@tillknowit/design-system";
import type { AreaMessages } from "@tillknowit/i18n";
import { useEffect, useState, type ReactNode } from "react";

const en = {
  heading: "Which supplier?",
  explain: "We could not find a supplier application for your business yet. If you have applied before, type the supplier id once. If you are new, use the application screen to apply (it fills the id in for you).",
  label: "Supplier id",
  save: "Use this supplier",
  invalid: "That does not look like an id. It is made of letters, numbers and dashes, for example 3f2b6c1e-0000-4000-8000-000000000000.",
  using: "Working as {name}.",
  usingId: "Working as supplier {id}.",
  change: "Change supplier",
  finding: "Finding your supplier...",
  noOrganisation: "Sign in as someone who works for a supplier to see this screen.",
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

export interface SupplierIdentity { supplierId: string; organisationId: string; name: string; status: string }

export type Lookup =
  | { state: "loading" }
  | { state: "found"; supplier: SupplierIdentity }
  | { state: "missing" }
  | { state: "error"; message: string };

/** Finds the supplier behind the signed-in organisation: GET /organisations/{id}/supplier. */
export function useSupplierLookup(): Lookup {
  const api = useApi();
  const { personId, supplierOrganisationId } = useSession();
  const [lookup, setLookup] = useState<Lookup>({ state: "loading" });
  useEffect(() => {
    let live = true;
    if (supplierOrganisationId === "" || personId === "") { setLookup({ state: "missing" }); return; }
    setLookup({ state: "loading" });
    void api.get<SupplierIdentity>(`/organisations/${supplierOrganisationId}/supplier`, { callerPersonId: personId }).then((r) => {
      if (!live) return;
      if (r.ok) { saveSupplierId(r.value.supplierId); setLookup({ state: "found", supplier: r.value }); }
      else if (r.code === "supplier.not_found") setLookup({ state: "missing" });
      else setLookup({ state: "error", message: r.message });
    });
    return () => { live = false; };
  }, [api, personId, supplierOrganisationId]);
  return lookup;
}

/** Shows its children once the supplier is known (looked up from the organisation). A typed id is only offered when the server has no supplier for it yet. */
export function SupplierGate({ children }: { children: (supplierId: string) => ReactNode }) {
  const t = useMessages(messages);
  const { supplierOrganisationId } = useSession();
  const lookup = useSupplierLookup();
  const [manual, setManual] = useState(readSupplierId);
  const [draft, setDraft] = useState("");
  const [invalid, setInvalid] = useState(false);

  if (lookup.state === "loading") return <p>{t("finding")}</p>;
  if (lookup.state === "error") return <Notice kind="danger">{lookup.message}</Notice>;

  if (lookup.state === "found") {
    return (
      <Stack>
        <span>{t("using", { name: lookup.supplier.name })}</span>
        {children(lookup.supplier.supplierId)}
      </Stack>
    );
  }

  if (manual === "") {
    return (
      <Card title={t("heading")}>
        <Stack>
          <p>{supplierOrganisationId === "" ? t("noOrganisation") : t("explain")}</p>
          <form onSubmit={(e) => {
            e.preventDefault();
            const v = draft.trim();
            if (!looksLikeId(v)) { setInvalid(true); return; }
            setInvalid(false);
            saveSupplierId(v);
            setManual(v);
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
        <span>{t("usingId", { id: manual })}</span>
        <Button variant="quiet" onClick={() => { saveSupplierId(""); setManual(""); }}>{t("change")}</Button>
      </Inline>
      {children(manual)}
    </Stack>
  );
}
