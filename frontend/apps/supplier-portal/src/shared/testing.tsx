import type { Api } from "@tillknowit/api-client";
import { fail, ok } from "@tillknowit/domain";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { configure, render } from "@testing-library/react";
import type { ReactElement } from "react";
import { vi } from "vitest";

export const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "org1", deviceId: "", language: "en" };
export const SUPPLIER = "3f2b6c1e-0000-4000-8000-000000000001";

/**
 * Mounts a screen with a fake Api (only the calls the test gives). The supplier lookup (GET /organisations/{id}/supplier) is answered here: it finds
 * SUPPLIER, or says supplier.not_found when withSupplier is false.
 */
export function mount(ui: ReactElement, api: Partial<Api>, withSupplier = true) {
  try { localStorage.clear(); } catch { /* ignore */ }
  const fake = {
    ...api,
    get: async (path: string, query?: unknown) => {
      if (/^\/organisations\/[^/]+\/supplier$/.test(path)) {
        return withSupplier ? ok({ supplierId: SUPPLIER, organisationId: "org1", name: "Cash and Carry", status: "verified" }) : fail("supplier.not_found", "This business has no supplier application yet.");
      }
      return api.get ? api.get(path, query as never) : fail("test.no_get", "No fake get given.");
    },
  };
  return render(<SessionProvider api={fake as unknown as Api} initial={session}>{ui}</SessionProvider>);
}

// Typing into forms is slow on a busy machine; a generous limit stops that being reported as a failure.
vi.setConfig({ testTimeout: 30_000 });
configure({ asyncUtilTimeout: 10_000 });
