import { Button, Notice } from "@tillknowit/design-system";
import { createApi, type Api } from "@tillknowit/api-client";
import { fail, flushOutbox, type CatalogueItem, type Outbox, type PromotionDef, type SendBatch } from "@tillknowit/domain";
import { Features } from "@tillknowit/packs";
import { isLanguage, languages, translate, translateArea, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BookkeepingOutbox } from "./bookkeepingOutbox";
import { loadCreditAccounts, type CreditAccount } from "./creditAccounts";
import { initialStatus, loadStatus, pullAll, startDownloadSync, type DownloadSyncHandle, type PullStatus } from "./downloadSync";
import type { SaleBridge, TillContext, TillHardware } from "./feature";
import { tillFeatures } from "./features";
import { IndexedDbOutbox } from "./indexedDbOutbox";
import { IndexedDbKeyValueStore, type KeyValueStore } from "./localData";
import { loadPack } from "./packStore";
import { sendPendingSerials } from "./pendingSerials";
import { PinUnlock } from "./PinUnlock";
import { RegisterScreen } from "./RegisterScreen";
import { SellScreen } from "./SellScreen";
import { loadCatalogue, loadPromotions } from "./catalogueStore";
import { loadStaff, type StaffRecord } from "./staffStore";
import { httpSend, loadDeviceConfig, startSync, type DeviceConfig, type SyncHandle } from "./syncRunner";
import { tm } from "./tillMessages";
import styles from "./App.module.css";

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

/** What the till is built from. The real ones are the defaults; tests hand in fakes (a fake network, a memory database, fake devices). */
export interface AppServices {
  kv: KeyValueStore;
  /** The upload queue, before the till's books are attached to it. */
  outbox: Outbox;
  api: Api;
  send: SendBatch;
  hardware: TillHardware;
}

export function App({ services }: { services?: Partial<AppServices> } = {}) {
  const [language, setLanguage] = useState<LanguageCode>(initialLanguage);
  const [device, setDevice] = useState<DeviceConfig | null>(() => loadDeviceConfig());
  const [skipped, setSkipped] = useState(false);
  const [staff, setStaff] = useState<StaffRecord[] | null>(null);
  const [user, setUser] = useState<StaffRecord | null>(null);
  const [catalogue, setCatalogue] = useState<CatalogueItem[]>([]);
  const [promotions, setPromotions] = useState<PromotionDef[]>([]);
  const [creditAccounts, setCreditAccounts] = useState<CreditAccount[]>([]);
  const [counts, setCounts] = useState({ waiting: 0, refused: 0 });
  const [serialProblem, setSerialProblem] = useState("");
  const [pull, setPull] = useState<PullStatus>(initialStatus);
  const kv = useMemo(() => services?.kv ?? new IndexedDbKeyValueStore(), [services?.kv]);
  const outbox = useMemo(() => new BookkeepingOutbox(services?.outbox ?? new IndexedDbOutbox(), kv), [services?.outbox, kv]);
  const send = useMemo(() => services?.send ?? httpSend(), [services?.send]);
  const sync = useRef<SyncHandle | null>(null);
  const download = useRef<DownloadSyncHandle | null>(null);
  const bridge = useRef<SaleBridge | null>(null);
  const userRef = useRef<StaffRecord | null>(null);
  userRef.current = user;
  const online = useOnline();
  const [tab, setTab] = useState("sell");
  const api = useMemo(() => services?.api ?? createApi(), [services?.api]);
  const hardware = useMemo<TillHardware>(() => services?.hardware ?? {}, [services?.hardware]);
  const [pack, setPack] = useState(() => loadPack(device?.shopId ?? ""));
  const features = useMemo(() => new Features(pack), [pack]);
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);
  const x = (key: Parameters<typeof tm>[1], params?: Record<string, string | number>) => tm(language, key, params);

  /** Reads what is stored on the till into what the screens show. Run at start and whenever a download changed something. */
  const reload = useCallback(async () => {
    setStaff(await loadStaff(kv));
    setCatalogue(await loadCatalogue(kv));
    setPromotions(await loadPromotions(kv));
    setPack(loadPack(loadDeviceConfig()?.shopId ?? ""));
    setPull(await loadStatus(kv));
  }, [kv]);

  useEffect(() => {
    void reload();
  }, [reload]);

  // The sales queue (and every other record waiting) is sent whenever there is a connection; selling never waits for it.
  useEffect(() => {
    const handle = startSync({ outbox, config: loadDeviceConfig, send, onCounts: setCounts });
    sync.current = handle;
    return () => handle.stop();
  }, [outbox, send]);

  // The download: staff, products, promotions and the pack come down, and a wipe request is checked, whenever there is a connection.
  useEffect(() => {
    if (!device) return;
    const handle = startDownloadSync({
      run: () => pullAll({ api, device, kv, outbox, staffId: () => userRef.current?.id ?? null, flush: async () => void (await flushOutbox(outbox, device, send).catch(() => undefined)) }),
      onResult: (result) => {
        setPull(result.status);
        if (result.wiped) {
          setDevice(null);
          setUser(null);
          setStaff([]);
          setCatalogue([]);
          setPromotions([]);
          setCreditAccounts([]);
          setPack(loadPack(""));
        } else if (result.changed.staff || result.changed.products || result.changed.promotions || result.changed.pack) void reload();
      },
    });
    download.current = handle;
    return () => handle.stop();
  }, [device, api, kv, outbox, send, reload]);

  // Serial numbers typed after a sale wait on the till until the sale has reached the server; this sends them as soon as it has.
  const sendSerials = useCallback(async () => {
    const who = userRef.current;
    if (!online || !device || !who) return;
    const result = await sendPendingSerials({ kv, api, outbox, caller: { shopId: device.shopId, deviceId: device.deviceId, staffId: who.id } });
    if (result.failed[0]) setSerialProblem(x("sell.serialsFailed", { message: result.failed[0].message }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [online, device, kv, api, outbox, language]);
  useEffect(() => {
    if (counts.waiting === 0) void sendSerials();
  }, [counts.waiting, sendSerials, user]);

  useEffect(() => {
    if (!online) return;
    sync.current?.nudge();
    download.current?.nudge();
  }, [online]);

  // The credit customers, for selling on credit: fresh when the till can reach the server, otherwise the copy it kept.
  useEffect(() => {
    let live = true;
    void loadCreditAccounts(api, device, kv).then((r) => live && setCreditAccounts(r.accounts));
    return () => { live = false; };
  }, [api, device, kv, online, pull.lastSyncAt]);

  useEffect(() => {
    document.documentElement.lang = language;
    try {
      localStorage.setItem(LANGUAGE_KEY, language);
    } catch {
      // Not saved; the choice lasts until the app closes.
    }
  }, [language]);

  const saleBridge = useMemo<SaleBridge>(() => ({
    addLine: (line) => bridge.current?.addLine(line) ?? fail("sale.screen_missing", "The sell screen is not ready."),
    addProduct: (id, quantity) => bridge.current?.addProduct(id, quantity) ?? fail("sale.screen_missing", "The sell screen is not ready."),
    setCustomer: (id, name) => bridge.current?.setCustomer(id, name),
  }), []);
  const onBridge = useCallback((b: SaleBridge | null) => { bridge.current = b; }, []);

  const registered = device !== null;
  const needsRegistration = !registered && !(import.meta.env.DEV && skipped);

  const banner = !registered ? null : pull.wipe === "waiting"
    ? <Notice kind="warning">{x("banner.wipeWaiting", { count: pull.waitingSales })}</Notice>
    : pull.deviceStatus === "pending" ? <Notice kind="warning">{x("banner.pending")}</Notice>
    : pull.deviceStatus === "disabled" ? <Notice kind="danger">{x("banner.disabled")}</Notice>
    : pull.lastError ? <Notice kind="warning">{x("banner.error", { message: pull.lastError })}</Notice>
    : serialProblem ? <Notice kind="warning">{serialProblem}</Notice>
    : null;

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <strong className={styles.brand}>{t("app.name")}</strong>
        <span className={online ? styles.online : styles.offline}>{online ? t("status.online") : t("status.offline")}</span>
        {counts.waiting > 0 && <span className={styles.offline}>{t("status.waiting", { count: counts.waiting })}</span>}
        {counts.refused > 0 && <span className={styles.offline}>{t("status.refused", { count: counts.refused })}</span>}
        {registered && (
          <>
            <span className={styles.online}>{pull.lastSyncAt ? x("sync.lastAt", { time: new Date(pull.lastSyncAt).toLocaleTimeString("en-ZA", { hour: "2-digit", minute: "2-digit" }) }) : x("sync.never")}</span>
            <Button variant="quiet" onClick={() => download.current?.nudge()} disabled={!online}>{x("sync.now")}</Button>
          </>
        )}
        <label className={styles.language}>
          <span className={styles.visuallyHidden}>{t("app.language")}</span>
          <select value={language} onChange={(e) => isLanguage(e.target.value) && setLanguage(e.target.value)}>
            {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>
      </header>

      <main className={styles.main}>
        {banner && <div className={styles.banner}>{banner}</div>}
        {needsRegistration ? (
          <RegisterScreen language={language} onRegistered={(d) => { setDevice(d); setPack(loadPack(d.shopId)); }} onSkip={import.meta.env.DEV ? () => setSkipped(true) : undefined} />
        ) : user ? (
          <>
            <div className={styles.welcome}>
              <strong>{t("till.hello", { name: user.name })}</strong>
              <Button variant="secondary" onClick={() => setUser(null)}>{t("till.lock")}</Button>
            </div>
            {(() => {
              const ctx: TillContext = {
                staff: user, language, features, api, device, online, outbox, catalogue, promotions, hardware, nudgeSync: () => sync.current?.nudge(),
                kv, shopName: pack.shopName, sale: saleBridge, openSell: () => setTab("sell"),
              };
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
                  {current && <current.component ctx={ctx} />}
                  {/* The sale in progress stays here, hidden, while another tab is showing, so switching tabs never loses it. */}
                  <div hidden={Boolean(current)}>
                    <SellScreen
                      staff={user} language={language} catalogue={catalogue} promotions={promotions}
                      barcodeRules={(() => { const r = features.barcodeRules(); return r.ok ? r.value : null; })()}
                      outbox={outbox} onSaleQueued={() => sync.current?.nudge()}
                      features={features} hardware={hardware} api={api} device={device} online={online} creditAccounts={creditAccounts} onBridge={onBridge}
                      shopName={pack.shopName} kv={kv} onSerialsQueued={() => { sync.current?.nudge(); void sendSerials(); }}
                    />
                  </div>
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
