import type { Api } from "@tillknowit/api-client";
import { MemoryOutbox, newId, type CatalogueItem, type SaleUploadDto } from "@tillknowit/domain";
import { Features, type ShopPack } from "@tillknowit/packs";
import { vi } from "vitest";
import { BookkeepingOutbox } from "./bookkeepingOutbox";
import type { TillContext, TillHardware } from "./feature";
import { MemoryKeyValueStore } from "./localData";
import type { StaffRecord } from "./staffStore";

export const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
export const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
export const STAFF = "0192a3b4-0000-7000-8000-00000000a001";

export const bread: CatalogueItem = { id: "p-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false };
export const milk: CatalogueItem = { id: "p-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false };
export const mince: CatalogueItem = { id: "p-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true };

export interface CoreCtxOptions {
  api?: unknown;
  flags?: Array<string | { flag: string; settingsJson: string }>;
  role?: StaffRecord["role"];
  online?: boolean;
  hardware?: TillHardware;
  catalogue?: CatalogueItem[];
  staffId?: string;
}

/** A till context for tests of the core tabs: a fake api (only the verbs a test gives), a memory database, and an outbox that keeps the till's books like the real one. */
export function makeCoreCtx(over: CoreCtxOptions = {}) {
  const pack: ShopPack = {
    shopId: SHOP, packCode: "test", packName: "Test", packVersion: 1, slipTemplateCode: "slip.sale",
    features: (over.flags ?? []).map((f) => (typeof f === "string" ? { flag: f } : f)),
  };
  const kv = new MemoryKeyValueStore();
  const raw = new MemoryOutbox();
  const outbox = new BookkeepingOutbox(raw, kv);
  const nudgeSync = vi.fn();
  const ctx: TillContext = {
    staff: { id: over.staffId ?? STAFF, name: "Thandi", role: over.role ?? "manager", pinHash: "x" },
    language: "en",
    features: new Features(pack),
    api: (over.api ?? {}) as Api,
    device: { shopId: SHOP, deviceId: DEVICE },
    online: over.online ?? true,
    outbox,
    catalogue: over.catalogue ?? [bread, milk, mince],
    promotions: [],
    hardware: over.hardware ?? {},
    nudgeSync,
    kv,
  };
  return { ctx, kv, outbox, raw, nudgeSync };
}

/** A sale as the till would have queued it, for tests of refunds, cancellations and the cash-up. */
export function aSale(over: Partial<SaleUploadDto> & { id?: string } = {}): SaleUploadDto {
  const id = over.id ?? newId(Date.now());
  return {
    id, staffId: STAFF, soldAt: new Date().toISOString(), paymentMethodCode: "cash", totalCents: 3_600, vatCents: 470, tenderedCents: 5_000, changeCents: 1_400, accountId: null,
    lines: [{ productId: bread.id, name: bread.name, quantity: 2, priceCents: 1800, costCents: 1200, unitDivisor: 1, discountCents: 0, promotionId: null }],
    payments: null, tipCents: 0, customerId: null, ...over,
  };
}
