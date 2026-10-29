import { defaultPack, newerPack, type ShopPack } from "@tillknowit/packs";

const KEY = "tillknowit.pack";

/** The pack this till last downloaded. Until one has been downloaded, the simplest till. */
export function loadPack(shopId: string): ShopPack {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) {
      const p = JSON.parse(raw) as ShopPack;
      if (p.shopId === shopId && Array.isArray(p.features)) return p;
    }
  } catch {
    // Storage unavailable: fall through to the default.
  }
  return defaultPack(shopId);
}

export function savePack(pack: ShopPack): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(pack));
  } catch {
    // The pack is downloaded again next time.
  }
}

export function clearPack(): void {
  try {
    localStorage.removeItem(KEY);
  } catch {
    // Nothing to clear.
  }
}

/** The pack as the server sends it (ShopPackResponse). */
export interface ServerPack {
  shopId: string;
  packCode: string;
  packName: string;
  packVersion: number;
  slipTemplateCode: string;
  features: Array<{ flag: string; settingsJson?: string | null }>;
}

export function mapPack(p: ServerPack): ShopPack {
  return {
    shopId: p.shopId, packCode: p.packCode, packName: p.packName, packVersion: p.packVersion, slipTemplateCode: p.slipTemplateCode,
    features: (p.features ?? []).map((f) => ({ flag: f.flag, settingsJson: f.settingsJson ?? null })),
  };
}

/**
 * The pack to keep when one arrives: a different business type always replaces the old one (its version is its own); the same type keeps the higher version.
 */
export function chooseIncomingPack(current: ShopPack, incoming: ShopPack): ShopPack {
  if (incoming.shopId !== current.shopId) return current;
  return incoming.packCode !== current.packCode ? incoming : newerPack(current, incoming);
}
