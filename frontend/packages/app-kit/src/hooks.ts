import { translateArea, type AreaMessages } from "@tillknowit/i18n";
import { useCallback, useEffect, useRef, useState } from "react";
import { useSession } from "./session";
import type { Result } from "@tillknowit/domain";

/** The translate function for one area's messages, in the language of the signed-in person. */
export function useMessages<K extends string>(area: AreaMessages<K>) {
  const { language } = useSession();
  return useCallback((key: K, params?: Record<string, string | number>) => translateArea(area, language, key, params), [area, language]);
}

export interface Loaded<T> {
  data: T | null;
  error: { code: string; message: string } | null;
  loading: boolean;
  reload: () => void;
}

/** Loads something when the screen opens (and whenever the inputs change) and keeps the answer, the error and the loading state. */
export function useLoad<T>(load: () => Promise<Result<T>>, inputs: readonly unknown[]): Loaded<T> {
  const [state, setState] = useState<{ data: T | null; error: Loaded<T>["error"]; loading: boolean }>({ data: null, error: null, loading: true });
  const [tick, setTick] = useState(0);
  const latest = useRef(0);

  useEffect(() => {
    const mine = ++latest.current;
    setState((s) => ({ ...s, loading: true }));
    void load().then((r) => {
      if (mine !== latest.current) return;   // a newer request has started: ignore this answer
      setState(r.ok ? { data: r.value, error: null, loading: false } : { data: null, error: { code: r.code, message: r.message }, loading: false });
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...inputs, tick]);

  return { ...state, reload: () => setTick((n) => n + 1) };
}

export interface Action<A extends unknown[], T> {
  run: (...args: A) => Promise<Result<T>>;
  busy: boolean;
  error: { code: string; message: string } | null;
  clear: () => void;
}

/** Wraps something the person does (a button, a form): while it runs the button is busy, and if it fails the error is kept for the screen to show. */
export function useAction<A extends unknown[], T>(act: (...args: A) => Promise<Result<T>>): Action<A, T> {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Action<A, T>["error"]>(null);
  const run = useCallback(async (...args: A) => {
    setBusy(true);
    setError(null);
    const r = await act(...args);
    setBusy(false);
    if (!r.ok) setError({ code: r.code, message: r.message });
    return r;
  }, [act]);
  return { run, busy, error, clear: () => setError(null) };
}
