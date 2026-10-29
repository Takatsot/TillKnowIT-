import { Button } from "@tillknowit/design-system";
import { isLanguage, languages, translate, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import type { CatalogueItem, PromotionDef } from "@tillknowit/domain";
import { createApi } from "@tillknowit/api-client";
import { Features } from "@tillknowit/packs";
import { translateArea } from "@tillknowit/i18n";
import { useEffect, useMemo, useRef, useState } from "react";
import type { TillContext } from "./feature";
import { tillFeatures } from "./features";
import { loadPack } from "./packStore";
import styles from "./App.module.css";
import { loadCatalogue, loadPromotions } from "./catalogueStore";
import { IndexedDbOutbox } from "./indexedDbOutbox";
import { PinUnlock } from "./PinUnlock";
import { SellScreen } from "./SellScreen";
import { loadStaff, type StaffRecord } from "./staffStore";
import { httpSend, loadDeviceConfig, startSync, type SyncHandle } from "./syncRunner";

const LANGUAGE_KEY = "tillknowit.language";

function initialLanguage(): LanguageCode {
  try {
    const saved = localStorage.getItem(LANGUAGE_KEY);
    if (saved && isLanguage(saved)) return saved;
  } catch {
    // Storage unavailable: start in English.
  }
  return "en";
}

function useOnline(): boolean {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const up = () => setOnline(true);
    const down = () => setOnline(false);
    window.addEventListener("online", up);
    window.addEventListener("offline", down);
    return () => {
      window.removeEventListener("online", up);
      window.removeEventListener("offline", down);
    };
  }, []);
  return online;
}

export function App() {
  const [language, setLanguage] = useState<LanguageCode>(initialLanguage);
  const [staff, setStaff] = useState<StaffRecord[] | null>(null);
  const [user, setUser] = useState<StaffRecord | null>(null);
  const [catalogue, setCatalogue] = useState<CatalogueItem[]>([]);
  const [promotions, setPromotions] = useState<PromotionDef[]>([]);
  const [counts, setCounts] = useState({ waiting: 0, refused: 0 });
  const outbox = useMemo(() => new IndexedDbOutbox(), []);
  const sync = useRef<SyncHandle | null>(null);
  const online = useOnline();
  const [tab, setTab] = useState("sell");
  const api = useMemo(() => createApi(), []);
  const device = useMemo(() => loadDeviceConfig(), []);
  const features = useMemo(() => new Features(loadPack(device?.shopId ?? "")), [device]);
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);

  useEffect(() => {
    void loadStaff().then(setStaff);
    void loadCatalogue().then(setCatalogue);
    void loadPromotions().then(setPromotions);
  }, []);

  // The sales queue is sent whenever there is a connection; selling never waits for it.
  useEffect(() => {
    const handle = startSync({ outbox, config: loadDeviceConfig, send: httpSend(), onCounts: setCounts });
    sync.current = handle;
    return () => handle.stop();
  }, [outbox]);

  useEffect(() => {
    if (online) sync.current?.nudge();
  }, [online]);

  useEffect(() => {
    document.documentElement.lang = language;
    try {
      localStorage.setItem(LANGUAGE_KEY, language);
    } catch {
      // Not saved; the choice lasts until the app closes.
    }
  }, [language]);

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <strong className={styles.brand}>{t("app.name")}</strong>
        <span className={online ? styles.online : styles.offline}>{online ? t("status.online") : t("status.offline")}</span>
        {counts.waiting > 0 && <span className={styles.offline}>{t("status.waiting", { count: counts.waiting })}</span>}
        {counts.refused > 0 && <span className={styles.offline}>{t("status.refused", { count: counts.refused })}</span>}
        <label className={styles.language}>
          <span className={styles.visuallyHidden}>{t("app.language")}</span>
          <select value={language} onChange={(e) => isLanguage(e.target.value) && setLanguage(e.target.value)}>
            {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>
      </header>

      <main className={styles.main}>
        {user ? (
          <>
            <div className={styles.welcome}>
              <strong>{t("till.hello", { name: user.name })}</strong>
              <Button variant="secondary" onClick={() => setUser(null)}>{t("till.lock")}</Button>
            </div>
            {(() => {
              const ctx: TillContext = { staff: user, language, features, api, device, online, outbox, catalogue, promotions, hardware: {}, nudgeSync: () => sync.current?.nudge() };
              const visible = tillFeatures.filter((f) => (f.flag === undefined || features.isOn(f.flag)) && (f.roles === undefined || f.roles.includes(user.role)));
              const current = visible.find((f) => f.id === tab);
              return (
                <>
                  {visible.length > 0 && (
                    <nav className={styles.tabs} aria-label={t("app.name")}>
                      <Button variant={current ? "quiet" : "primary"} onClick={() => setTab("sell")} aria-current={current ? undefined : "page"}>{t("tab.sell")}</Button>
                      {visible.map((f) => (
                        <Button key={f.id} variant={current?.id === f.id ? "primary" : "quiet"} disabled={f.requiresOnline && !online} onClick={() => setTab(f.id)} aria-current={current?.id === f.id ? "page" : undefined}>
                          {translateArea(f.messages, language, f.title)}
                        </Button>
                      ))}
                    </nav>
                  )}
                  {current
                    ? <current.component ctx={ctx} />
                    : <SellScreen staff={user} language={language} catalogue={catalogue} promotions={promotions} barcodeRules={(() => { const r = features.barcodeRules(); return r.ok ? r.value : null; })()} outbox={outbox} onSaleQueued={() => sync.current?.nudge()} />}
                </>
              );
            })()}
          </>
        ) : staff ? (
          <PinUnlock staff={staff} language={language} onUnlock={setUser} />
        ) : null}
      </main>
    </div>
  );
}
