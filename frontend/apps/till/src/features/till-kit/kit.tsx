import type { Api } from "@tillknowit/api-client";
import { translateArea, type AreaMessages, type LanguageCode } from "@tillknowit/i18n";
import { Notice } from "@tillknowit/design-system";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { Result } from "@tillknowit/domain";
import type { TillContext } from "../../feature";
import { kitMessages, type KitKey } from "./messages";

export type Translate<K extends string> = (key: K, params?: Record<string, string | number>) => string;

/** The translate function of one tab, in the language the till is set to. */
export function useT<K extends string>(messages: AreaMessages<K>, language: LanguageCode): Translate<K> {
  return useMemo(() => (key: K, params?: Record<string, string | number>) => translateArea(messages, language, key, params), [messages, language]);
}

export const useKitT = (language: LanguageCode): Translate<KitKey> => useT(kitMessages, language);

/** Who is calling: the server wants the shop, this till and the cashier on nearly every counter route. */
export interface Caller {
  shopId: string;
  deviceId: string;
  staffId: string;
}

export function callerOf(ctx: TillContext): Caller | null {
  return ctx.device ? { shopId: ctx.device.shopId, deviceId: ctx.device.deviceId, staffId: ctx.staff.id } : null;
}

export type Failure = { code: string; message: string };

/** Words for the person. The server's own message is kept for everything but the two cases that need friendlier wording. */
export function explain(error: Failure, t: Translate<KitKey>): string {
  if (error.code === "plan.feature_not_included") return t("kit.planMissing");
  if (error.code === "feature.not_enabled") return t("kit.notEnabled");
  return error.message;
}

export interface Loaded<T> {
  data: T | null;
  error: Failure | null;
  loading: boolean;
  reload: () => void;
}

/** Loads when the tab opens and whenever the inputs change; a slow older answer never replaces a newer one. */
export function useLoad<T>(load: () => Promise<Result<T>>, inputs: readonly unknown[]): Loaded<T> {
  const [state, setState] = useState<{ data: T | null; error: Failure | null; loading: boolean }>({ data: null, error: null, loading: true });
  const [tick, setTick] = useState(0);
  const latest = useRef(0);
  useEffect(() => {
    const mine = ++latest.current;
    setState((s) => ({ ...s, loading: true }));
    void load().then((r) => {
      if (mine !== latest.current) return;
      setState(r.ok ? { data: r.value, error: null, loading: false } : { data: null, error: { code: r.code, message: r.message }, loading: false });
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...inputs, tick]);
  return { ...state, reload: useCallback(() => setTick((n) => n + 1), []) };
}

/** Something the person does. While it runs the screen is busy; a refusal is kept for the screen to show, never thrown. */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Failure | null>(null);
  const run = useCallback(async <T,>(act: () => Promise<Result<T>>): Promise<Result<T>> => {
    setBusy(true);
    setError(null);
    const r = await act();
    setBusy(false);
    if (!r.ok) setError({ code: r.code, message: r.message });
    return r;
  }, []);
  return { run, busy, error, clear: () => setError(null), fail: (e: Failure) => setError(e) };
}

export function ErrorNotice({ error, t }: { error: Failure | null; t: Translate<KitKey> }) {
  return error ? <Notice kind="danger">{explain(error, t)}</Notice> : null;
}

/** Wraps a tab: says plainly when the till has no shop or no connection, so the tab never fails silently. */
export function TabGate({ ctx, children }: { ctx: TillContext; children: (caller: Caller, api: Api) => ReactNode }) {
  const t = useKitT(ctx.language);
  const caller = callerOf(ctx);
  if (!caller) return <Notice kind="warning">{t("kit.noDevice")}</Notice>;
  return (
    <>
      {ctx.online ? null : <Notice kind="warning">{t("kit.offline")}</Notice>}
      {children(caller, ctx.api)}
    </>
  );
}

/** The device and staff pair, for the routes that take them in the query string or in the body. */
export const who = (c: Caller) => ({ deviceId: c.deviceId, staffId: c.staffId });

/** Hands a downloaded file (a quote as PDF) to the browser. */
export function saveFile(file: { blob: Blob; fileName: string }): void {
  const url = URL.createObjectURL(file.blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = file.fileName;
  a.click();
  URL.revokeObjectURL(url);
}

/** A reference the person can keep or change: the letter and five digits from the clock, so two tills rarely pick the same. */
export const suggestRef = (prefix: string, now: number = Date.now()): string => `${prefix}${String(Math.floor(now / 1000) % 100_000).padStart(5, "0")}`;
