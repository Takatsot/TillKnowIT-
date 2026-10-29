import { createApi, type Api } from "@tillknowit/api-client";
import type { Result } from "@tillknowit/domain";
import { isLanguage, type LanguageCode } from "@tillknowit/i18n";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

/**
 * Who is signed in and which business they are looking at. The person, token and name come from OTP sign-in (see SignIn.tsx); the token is sent with every
 * request by the api client. shopId / supplierOrganisationId are chosen from the person's memberships by the Shell, with a manual fallback.
 */
export interface Session {
  personId: string;
  /** The shop (or head shop) being managed. */
  shopId: string;
  /** The supplier organisation, for the supplier portal. */
  supplierOrganisationId: string;
  /** This device, for screens that act as a till. */
  deviceId: string;
  language: LanguageCode;
  /** The access token from sign-in. Empty or missing means signed out. */
  token?: string;
  /** When the token stops working (ISO 8601). */
  expiresAt?: string;
  displayName?: string;
}

export const emptySession: Session = { personId: "", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en", token: "", expiresAt: "", displayName: "" };

const KEY = "tillknowit.session";

/** Signed in means there is a person and a token that has not run out. */
export function isSignedIn(session: Session, now: Date = new Date()): boolean {
  if (!session.token || !session.personId) return false;
  const t = session.expiresAt ? Date.parse(session.expiresAt) : NaN;
  return Number.isFinite(t) && t > now.getTime();
}

export function loadSession(storage: Pick<Storage, "getItem"> = localStorage, now: Date = new Date()): Session {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return emptySession;
    const p = JSON.parse(raw) as Partial<Session>;
    const text = (v: unknown) => (typeof v === "string" ? v : "");
    const language: LanguageCode = typeof p.language === "string" && isLanguage(p.language) ? p.language : "en";
    const loaded: Session = {
      personId: text(p.personId), shopId: text(p.shopId), supplierOrganisationId: text(p.supplierOrganisationId), deviceId: text(p.deviceId), language,
      token: text(p.token), expiresAt: text(p.expiresAt), displayName: text(p.displayName),
    };
    // An expired token counts as signed out: nothing of the person is kept, only the device and language.
    return isSignedIn(loaded, now) ? loaded : { ...emptySession, deviceId: loaded.deviceId, language };
  } catch {
    return emptySession;
  }
}

export function saveSession(session: Session, storage: Pick<Storage, "setItem"> = localStorage): void {
  try {
    storage.setItem(KEY, JSON.stringify(session));
  } catch {
    // Storage unavailable: the session lasts until the page closes.
  }
}

/** What the server answers to POST /auth/otp/verify. */
export interface SignInAnswer {
  accessToken: string;
  expiresAt: string;
  personId: string;
  displayName: string;
  language: string;
}

export type SignOutReason = "ended" | "expired";

interface Ctx {
  session: Session;
  setSession: (update: Partial<Session>) => void;
  api: Api;
  signIn: (answer: SignInAnswer) => void;
  /** Signs out. A reason is shown on the sign-in screen ("please sign in again"). */
  signOut: (reason?: SignOutReason) => void;
  signOutReason: SignOutReason | null;
}

const SessionContext = createContext<Ctx | null>(null);

/** Wraps an api so that a refusal with auth.required (the token is missing, wrong or has run out) signs the person out. */
function withSignOutOnRefusal(base: Api, onRefused: () => void): Api {
  const watch = <T,>(p: Promise<Result<T>>): Promise<Result<T>> => p.then((r) => { if (!r.ok && r.code === "auth.required") onRefused(); return r; });
  return {
    // Arguments are passed on exactly as given, so a fake api in a test sees the same call it would without the wrapper.
    get: (...args: Parameters<Api["get"]>) => watch(base.get(...args)),
    post: (...args: Parameters<Api["post"]>) => watch(base.post(...args)),
    put: (...args: Parameters<Api["put"]>) => watch(base.put(...args)),
    patch: (...args: Parameters<Api["patch"]>) => watch(base.patch(...args)),
    delete: (...args: Parameters<Api["delete"]>) => watch(base.delete(...args)),
    upload: (...args: Parameters<Api["upload"]>) => watch(base.upload(...args)),
    download: (...args: Parameters<Api["download"]>) => watch(base.download(...args)),
  } as Api;
}

export function SessionProvider({ children, api, initial }: { children: ReactNode; api?: Api; initial?: Session }) {
  const [session, setState] = useState<Session>(() => initial ?? loadSession());
  const [signOutReason, setReason] = useState<SignOutReason | null>(null);
  const latest = useRef(session);
  latest.current = session;

  const setSession = useCallback((update: Partial<Session>) => {
    setState((s) => {
      const next = { ...s, ...update };
      saveSession(next);
      return next;
    });
  }, []);

  const signOut = useCallback((reason?: SignOutReason) => {
    setState((s) => {
      const next: Session = { ...emptySession, deviceId: s.deviceId, language: s.language };
      saveSession(next);
      return next;
    });
    setReason(reason ?? null);
  }, []);

  const signIn = useCallback((answer: SignInAnswer) => {
    setReason(null);
    setState((s) => {
      const next: Session = {
        ...emptySession, deviceId: s.deviceId, language: isLanguage(answer.language) ? answer.language : s.language,
        personId: answer.personId, token: answer.accessToken, expiresAt: answer.expiresAt, displayName: answer.displayName,
      };
      saveSession(next);
      return next;
    });
  }, []);

  const client = useMemo(() => {
    const base = api ?? createApi({ getToken: () => latest.current.token });
    return withSignOutOnRefusal(base, () => { if (latest.current.token) signOut("ended"); });
  }, [api, signOut]);

  // When the token runs out while the app is open, sign out then.
  useEffect(() => {
    if (!session.token || !session.expiresAt) return;
    const wait = Date.parse(session.expiresAt) - Date.now();
    if (!Number.isFinite(wait)) return;
    const timer = setTimeout(() => signOut("expired"), Math.min(Math.max(wait, 0), 2_147_000_000));
    return () => clearTimeout(timer);
  }, [session.token, session.expiresAt, signOut]);

  const value = useMemo(() => ({ session, setSession, api: client, signIn, signOut, signOutReason }), [session, setSession, client, signIn, signOut, signOutReason]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

function useCtx(): Ctx {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("Wrap the app in <SessionProvider>.");
  return ctx;
}

export const useSession = (): Session => useCtx().session;
export const useSetSession = () => useCtx().setSession;
export const useApi = (): Api => useCtx().api;
export const useSignIn = () => useCtx().signIn;
export const useSignOut = () => useCtx().signOut;
export const useSignOutReason = () => useCtx().signOutReason;
