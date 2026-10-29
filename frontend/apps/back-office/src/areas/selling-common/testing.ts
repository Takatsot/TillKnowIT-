import type { Api } from "@tillknowit/api-client";
import { ok } from "@tillknowit/domain";
import { configure } from "@testing-library/react";
import { vi } from "vitest";

// The till caller loads its lists in a few steps, which is slow on a busy machine; the default 1 second wait is too tight.
configure({ asyncUtilTimeout: 5000 });
vi.setConfig({ testTimeout: 30_000 });

/**
 * For tests: wraps a fake Api so the till caller's own lookups are answered (one approved till and the person's membership at the shop);
 * everything else goes to the fake. Not used by the app.
 */
export function withCaller(api: Partial<Api>, who: { deviceId?: string; staffId?: string; shopId?: string } = {}): Api {
  const { deviceId = "d1", staffId = "p1", shopId = "s1" } = who;
  return {
    ...api,
    get: (async (path: string, query?: unknown) => {
      if (path === `/shops/${shopId}/devices`) return ok([{ deviceId, name: "Till 1", status: "approved", lastSeenAt: null, wipeRequested: false }]);
      if (/^\/people\/[^/]+\/memberships$/.test(path)) return ok([{ membershipId: staffId, organisationId: shopId, organisationName: "Shop", organisationType: "shop", roleCode: "manager" }]);
      return api.get ? (api.get as (p: string, q?: unknown) => Promise<unknown>)(path, query) : ok(undefined);
    }) as Api["get"],
  } as Api;
}
