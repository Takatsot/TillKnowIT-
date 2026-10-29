import { Button, Card, Notice, TextField } from "@tillknowit/design-system";
import { languages, translateArea, isLanguage, type AreaMessages } from "@tillknowit/i18n";
import { useMemo, useState } from "react";
import type { Section } from "./section";
import { useSession, useSetSession } from "./session";

type ShellKey = "app" | "setup" | "personId" | "shopId" | "supplierId" | "deviceId" | "language" | "standIn" | "missing" | "save" | "noSections";

const shellMessages: AreaMessages<ShellKey> = {
  en: {
    app: "TillKnowIT", setup: "Who are you working as?", personId: "Your person id", shopId: "Shop id", supplierId: "Supplier organisation id", deviceId: "Device id",
    language: "Language", standIn: "Sign-in is not built yet. For now, type the ids you were given. The server trusts what you type, so this is for testing only.",
    missing: "Fill in {what} to use this part.", save: "Save", noSections: "Nothing is available yet.",
  },
};

/** The frame every app uses: a menu of its sections, the language choice, and the stand-in sign-in. */
export function Shell({ title, sections }: { title: string; sections: readonly Section<string>[] }) {
  const session = useSession();
  const setSession = useSetSession();
  const ordered = useMemo(() => [...sections].sort((a, b) => a.order - b.order), [sections]);
  const [openId, setOpenId] = useState<string | null>(null);
  const t = (k: ShellKey, p?: Record<string, string | number>) => translateArea(shellMessages, session.language, k, p);
  const [draft, setDraft] = useState({ personId: session.personId, shopId: session.shopId, supplierOrganisationId: session.supplierOrganisationId, deviceId: session.deviceId });

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
      </nav>
      <main style={{ padding: "var(--space-6)", display: "grid", gap: "var(--space-4)", alignContent: "start" }}>
        <Card title={t("setup")}>
          <Notice kind="warning">{t("standIn")}</Notice>
          <form
            onSubmit={(e) => { e.preventDefault(); setSession(draft); }}
            style={{ display: "grid", gap: "var(--space-3)", gridTemplateColumns: "repeat(auto-fit, minmax(14rem, 1fr))", alignItems: "end" }}
          >
            <TextField label={t("personId")} value={draft.personId} onChange={(e) => setDraft({ ...draft, personId: e.target.value.trim() })} />
            <TextField label={t("shopId")} value={draft.shopId} onChange={(e) => setDraft({ ...draft, shopId: e.target.value.trim() })} />
            <TextField label={t("supplierId")} value={draft.supplierOrganisationId} onChange={(e) => setDraft({ ...draft, supplierOrganisationId: e.target.value.trim() })} />
            <TextField label={t("deviceId")} value={draft.deviceId} onChange={(e) => setDraft({ ...draft, deviceId: e.target.value.trim() })} />
            <Button type="submit">{t("save")}</Button>
          </form>
        </Card>
        {!Component ? <p>{t("noSections")}</p> : missing.length > 0
          ? <Notice kind="warning">{t("missing", { what: missing.join(", ") })}</Notice>
          : <Component />}
      </main>
    </div>
  );
}
