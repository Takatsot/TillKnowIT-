import { parseBarcodeRules, type EmbeddedBarcodeRules, type Result } from "@tillknowit/domain";

/**
 * Business packs on the till. A pack is data the server sends (see ShopPackResponse in C#): which features are on and their settings. The selling screens ask this
 * package whether to show something; they never name a pack ("cafe", "salon") and never decide for themselves what a business type gets.
 */

/** The feature flags, named exactly as the server's FeatureFlags. A flag the till does not know about is ignored, never an error: the server may be newer. */
export const featureFlags = [
  "split_payments", "refunds", "open_tickets", "tables", "kitchen", "tips", "variants", "modifiers", "promotions", "weight", "price_embedded_barcodes",
  "appointments", "commission", "quotes", "layby", "serial_numbers", "customers", "loyalty", "vouchers", "printer", "cash_drawer", "scale", "message_bundles", "school_wallet",
] as const;

export type FeatureFlag = (typeof featureFlags)[number];

export interface PackFeature {
  flag: string;
  settingsJson?: string | null;
}

/** What the till downloads for its shop. */
export interface ShopPack {
  shopId: string;
  packCode: string;
  packName: string;
  packVersion: number;
  slipTemplateCode: string;
  features: readonly PackFeature[];
  /** The shop's own name, printed on slips. Missing in a pack saved before the server sent it. */
  shopName?: string;
}

/** The pack of a till that has not downloaded one yet or is offline for good: the simplest till, nothing extra switched on. */
export const defaultPack = (shopId: string): ShopPack => ({ shopId, packCode: "spaza", packName: "Spaza shop or general dealer", packVersion: 0, slipTemplateCode: "slip.sale", features: [] });

export class Features {
  private readonly byFlag: ReadonlyMap<string, PackFeature>;

  constructor(readonly pack: ShopPack) {
    this.byFlag = new Map(pack.features.map((f) => [f.flag, f]));
  }

  isOn(flag: FeatureFlag): boolean {
    return this.byFlag.has(flag);
  }

  /** The raw settings JSON of a feature, or null when it is off or has none. */
  settings(flag: FeatureFlag): string | null {
    return this.byFlag.get(flag)?.settingsJson ?? null;
  }

  /** The scale label rules, only when the shop uses price-embedded barcodes. */
  barcodeRules(): Result<EmbeddedBarcodeRules> {
    return parseBarcodeRules(this.isOn("price_embedded_barcodes") ? this.settings("price_embedded_barcodes") : null);
  }

  /**
   * The payment methods the cashier can choose from. Cash, card, QR and credit are always there; the rest follow the pack. Lay-by is a payment method
   * of its own, and so is a split between methods.
   */
  paymentMethods(): string[] {
    return ["cash", "card", "qr", "credit", ...(this.isOn("split_payments") ? ["split"] : []), ...(this.isOn("layby") ? ["layby"] : [])];
  }

  /** Whether a till with this pack needs a given device. The settings screen uses this to ask only for the hardware a business can use. */
  needs(device: "printer" | "cash_drawer" | "scale" | "kitchen"): boolean {
    return device === "kitchen" ? this.isOn("kitchen") : this.isOn(device);
  }

  /** The unknown flags the server sent: for the support screen, not for selling. */
  unknownFlags(): string[] {
    return this.pack.features.map((f) => f.flag).filter((f) => !(featureFlags as readonly string[]).includes(f));
  }
}

/**
 * The pack the till should sell with when a newer one arrives: the higher version wins; the same version keeps what is there, so a till never flips between two
 * copies of one version.
 */
export function newerPack(current: ShopPack, incoming: ShopPack): ShopPack {
  return incoming.shopId === current.shopId && incoming.packVersion > current.packVersion ? incoming : current;
}
