import { defaultPack, type ShopPack } from "@tillknowit/packs";

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
