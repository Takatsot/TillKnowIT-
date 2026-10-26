import { Button } from "@tillknowit/design-system";
import { isLanguage, languages, translate, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import { useEffect, useState } from "react";
import styles from "./App.module.css";
import { PinUnlock } from "./PinUnlock";
import { loadStaff, type StaffRecord } from "./staffStore";

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
  const online = useOnline();
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);

  useEffect(() => {
    void loadStaff().then(setStaff);
  }, []);

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
        <label className={styles.language}>
          <span className={styles.visuallyHidden}>{t("app.language")}</span>
          <select value={language} onChange={(e) => isLanguage(e.target.value) && setLanguage(e.target.value)}>
            {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>
      </header>

      <main className={styles.main}>
        {user ? (
          <section className={styles.welcome}>
            <h1>{t("till.hello", { name: user.name })}</h1>
            <p>{t("till.unlocked")}</p>
            <Button variant="secondary" onClick={() => setUser(null)}>{t("till.lock")}</Button>
          </section>
        ) : staff ? (
          <PinUnlock staff={staff} language={language} onUnlock={setUser} />
        ) : null}
      </main>
    </div>
  );
}
