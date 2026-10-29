import { Button, Card, Notice, SelectField, TextField } from "@tillknowit/design-system";
import { languages, translateArea, isLanguage } from "@tillknowit/i18n";
import { useEffect, useMemo, useState } from "react";
import { useLoad } from "./hooks";
import type { Section } from "./section";
import { SignIn } from "./SignIn";
import { isSignedIn, useApi, useSession, useSetSession, useSignOut } from "./session";
import { shellText, type ShellKey } from "./shellMessages";

/** One row of GET /people/{personId}/memberships. */
interface Membership { membershipId: string; organisationId: string; organisationName: string; organisationType: string; roleCode: string }

const kindOf = (m: Membership): "shop" | "supplier" | "other" => {
  const type = m.organisationType.toLowerCase();
  return type === "shop" ? "shop" : type === "supplier" ? "supplier" : "other";
};

/** Which organisation the signed-in person is working for. Choosing one sets the shop id or the supplier organisation id for every screen. */
function WorkingFor({ t }: { t: (k: ShellKey, p?: Record<string, string | number>) => string }) {
  const api = useApi();
  const session = useSession();
  const setSession = useSetSession();
  const memberships = useLoad(() => api.get<Membership[]>(`/people/${session.personId}/memberships`), [session.personId]);
  const [chosen, setChosen] = useState("");

  const options = useMemo(() => {
    const seen = new Map<string, Membership>();
    for (const m of memberships.data ?? []) if (kindOf(m) !== "other" && !seen.has(m.organisationId)) seen.set(m.organisationId, m);
    return [...seen.values()];
  }, [memberships.data]);

  // Fill in automatically: keep what is set if the person really belongs there, otherwise take their first shop and first supplier.
  useEffect(() => {
    if (!memberships.data) return;
    const shops = options.filter((m) => kindOf(m) === "shop");
    const suppliers = options.filter((m) => kindOf(m) === "supplier");
    const shopId = shops.some((m) => m.organisationId === session.shopId) ? session.shopId : shops[0]?.organisationId ?? "";
    const supplierOrganisationId = suppliers.some((m) => m.organisationId === session.supplierOrganisationId) ? session.supplierOrganisationId : suppliers[0]?.organisationId ?? "";
    if (shopId !== session.shopId || supplierOrganisationId !== session.supplierOrganisationId) setSession({ shopId, supplierOrganisationId });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [memberships.data]);

  const current = options.some((m) => m.organisationId === chosen) ? chosen
    : options.find((m) => m.organisationId === session.shopId)?.organisationId ?? options.find((m) => m.organisationId === session.supplierOrganisationId)?.organisationId ?? options[0]?.organisationId ?? "";

  function choose(id: string) {
    const m = options.find((o) => o.organisationId === id);
    if (!m) return;
    setChosen(id);
    setSession(kindOf(m) === "shop" ? { shopId: id } : { supplierOrganisationId: id });
  }

  if (memberships.error) return <Notice kind="danger">{t("membershipsFailed")} {memberships.error.message}</Notice>;
  if (memberships.loading) return null;
  if (options.length === 0) return <Notice kind="info">{t("workingForNone")}</Notice>;
  return (
    <SelectField label={t("workingFor")} value={current} onChange={(e) => choose(e.target.value)}>
      {options.map((m) => (
        <option key={m.organisationId} value={m.organisationId}>{m.organisationName} ({t(kindOf(m) === "shop" ? "orgShop" : "orgSupplier")})</option>
      ))}
    </SelectField>
  );
}

/** The frame every app uses: sign-in, then a menu of the app's sections, the language choice and who the person is working for. */
export function Shell({ title, sections }: { title: string; sections: readonly Section<string>[] }) {
  const session = useSession();
  const setSession = useSetSession();
  const signOut = useSignOut();
  const ordered = useMemo(() => [...sections].sort((a, b) => a.order - b.order), [sections]);
  const [openId, setOpenId] = useState<string | null>(null);
  const t = (k: ShellKey, p?: Record<string, string | number>) => shellText(session.language, k, p);
  const [draft, setDraft] = useState({ shopId: session.shopId, supplierOrganisationId: session.supplierOrganisationId, deviceId: session.deviceId });
  useEffect(() => { setDraft({ shopId: session.shopId, supplierOrganisationId: session.supplierOrganisationId, deviceId: session.deviceId }); }, [session.shopId, session.supplierOrganisationId, session.deviceId]);

  if (!isSignedIn(session)) return <SignIn />;

  const open = ordered.find((s) => s.id === openId) ?? ordered[0];
  const missing = open?.needs?.filter((n) => !session[n]) ?? [];
  const Component = open?.component;

  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(11rem, 14rem) 1fr", minHeight: "100vh" }}>
      <nav aria-label={title} style={{ padding: "var(--space-4)", borderRight: "1px solid var(--color-border)", display: "grid", gap: "var(--space-2)", alignContent: "start" }}>
        <strong>{title}</strong>
        {ordered.map((s) => (
          <Button key={s.id} variant={open?.id === s.id ? "primary" : "quiet"} onClick={() => setOpenId(s.id)} aria-current={open?.id === s.id ? "page" : undefined}>
            {translateArea(s.messages, session.language, s.title)}
          </Button>
        ))}
        <label style={{ display: "grid", gap: "var(--space-1)", fontSize: "var(--font-size-small)" }}>
          {t("language")}
          <select value={session.language} onChange={(e) => isLanguage(e.target.value) && setSession({ language: e.target.value })}>
            {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>
        <div style={{ display: "grid", gap: "var(--space-1)", fontSize: "var(--font-size-small)" }}>
          <span>{t("signedInAs")}</span>
          <strong>{session.displayName || session.personId}</strong>
          <Button variant="secondary" onClick={() => signOut()}>{t("signOut")}</Button>
        </div>
      </nav>
      <main style={{ padding: "var(--space-6)", display: "grid", gap: "var(--space-4)", alignContent: "start" }}>
        <Card title={t("workingFor")}>
          <TextField label={t("signedInAs")} value={session.displayName || session.personId} readOnly />
          <WorkingFor t={t} />
          <details>
            <summary>{t("advanced")}</summary>
            <p>{t("advancedHint")}</p>
            <form
              onSubmit={(e) => { e.preventDefault(); setSession(draft); }}
              style={{ display: "grid", gap: "var(--space-3)", gridTemplateColumns: "repeat(auto-fit, minmax(14rem, 1fr))", alignItems: "end" }}
            >
              <TextField label={t("shopId")} value={draft.shopId} onChange={(e) => setDraft({ ...draft, shopId: e.target.value.trim() })} />
              <TextField label={t("supplierId")} value={draft.supplierOrganisationId} onChange={(e) => setDraft({ ...draft, supplierOrganisationId: e.target.value.trim() })} />
              <TextField label={t("deviceId")} value={draft.deviceId} onChange={(e) => setDraft({ ...draft, deviceId: e.target.value.trim() })} />
              <Button type="submit">{t("save")}</Button>
            </form>
          </details>
        </Card>
        {!Component ? <p>{t("noSections")}</p> : missing.length > 0
          ? <Notice kind="warning">{t("missing", { what: missing.join(", ") })}</Notice>
          : <Component />}
      </main>
    </div>
  );
}
