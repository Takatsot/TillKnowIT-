import type { Api } from "@tillknowit/api-client";
import { MemoryOutbox, type CatalogueItem } from "@tillknowit/domain";
import { Features, type ShopPack } from "@tillknowit/packs";
import { vi } from "vitest";
import type { TillContext, TillHardware } from "../../feature";

export const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
export const milk: CatalogueItem = { id: "p-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false };
export const mince: CatalogueItem = { id: "p-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true };

export const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
export const STAFF = "0192a3b4-0000-7000-8000-00000000a001";
export const SHOP = "0192a3b4-0000-7000-8000-00000000b001";

/** A till context for tests: the api is a fake (only the verbs the test gives are there), the pack has the listed flags on. */
export function makeCtx(over: { api?: Partial<Api>; flags?: Array<string | { flag: string; settingsJson: string }>; hardware?: TillHardware; online?: boolean; device?: TillContext["device"] } = {}) {
  const pack: ShopPack = {
    shopId: SHOP, packCode: "test", packName: "Test", packVersion: 1, slipTemplateCode: "slip.sale",
    features: (over.flags ?? []).map((f) => (typeof f === "string" ? { flag: f } : f)),
  };
  const outbox = new MemoryOutbox();
  const nudgeSync = vi.fn();
  const ctx: TillContext = {
    staff: { id: STAFF, name: "Thandi", role: "cashier", pinHash: "x" },
    language: "en",
    features: new Features(pack),
    api: (over.api ?? {}) as Api,
    device: over.device === undefined ? { shopId: SHOP, deviceId: DEVICE } : over.device,
    online: over.online ?? true,
    outbox,
    catalogue: [bread, milk, mince],
    promotions: [],
    hardware: over.hardware ?? {},
    nudgeSync,
  };
  return { ctx, outbox, nudgeSync };
}
