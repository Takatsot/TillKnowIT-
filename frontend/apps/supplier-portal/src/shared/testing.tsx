import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { render } from "@testing-library/react";
import type { ReactElement } from "react";

export const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "org1", deviceId: "", language: "en" };
export const SUPPLIER = "3f2b6c1e-0000-4000-8000-000000000001";

/** Mounts a screen with a fake Api (only the calls the test gives) and a remembered supplier id. */
export function mount(ui: ReactElement, api: Partial<Api>, withSupplier = true) {
  try { localStorage.clear(); if (withSupplier) localStorage.setItem("tillknowit.supplierId", SUPPLIER); } catch { /* ignore */ }
  return render(<SessionProvider api={api as Api} initial={session}>{ui}</SessionProvider>);
}
