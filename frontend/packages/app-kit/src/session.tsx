import { createApi, type Api } from "@tillknowit/api-client";
import { isLanguage, type LanguageCode } from "@tillknowit/i18n";
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

/**
 * Who is using the app and which business they are looking at. TODO(auth): until OTP sign-in exists this is typed in on a setup screen and the server trusts it,
 * exactly as it trusts the person id in each request today. It is a stand-in, kept in one place so it can be replaced by the signed-in person.
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
}

export const emptySession: Session = { personId: "", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };

const KEY = "tillknowit.session";

export function loadSession(storage: Pick<Storage, "getItem"> = localStorage): Session {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return emptySession;
    const p = JSON.parse(raw) as Partial<Session>;
    const text = (v: unknown) => (typeof v === "string" ? v : "");
    return {
      personId: text(p.personId), shopId: text(p.shopId), supplierOrganisationId: text(p.supplierOrganisationId), deviceId: text(p.deviceId),
      language: typeof p.language === "string" && isLanguage(p.language) ? p.language : "en",
    };
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

interface Ctx {
  session: Session;
  setSession: (update: Partial<Session>) => void;
  api: Api;
}

const SessionContext = createContext<Ctx | null>(null);

export function SessionProvider({ children, api, initial }: { children: ReactNode; api?: Api; initial?: Session }) {
  const [session, setState] = useState<Session>(() => initial ?? loadSession());
  const client = useMemo(() => api ?? createApi(), [api]);
  const setSession = useCallback((update: Partial<Session>) => {
    setState((s) => {
      const next = { ...s, ...update };
      saveSession(next);
      return next;
    });
  }, []);
  const value = useMemo(() => ({ session, setSession, api: client }), [session, setSession, client]);
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
