import type { Api } from "@tillknowit/api-client";
import type { CatalogueItem, Outbox, PromotionDef, Result } from "@tillknowit/domain";
import type { ICashDrawer, IKitchenDisplay, IPrinter, IScale } from "@tillknowit/hardware";
import type { AreaMessages, LanguageCode } from "@tillknowit/i18n";
import type { FeatureFlag, Features } from "@tillknowit/packs";
import type { ComponentType } from "react";
import type { KeyValueStore } from "./localData";
import type { StaffRecord } from "./staffStore";
import type { DeviceConfig } from "./syncRunner";

/** The devices this till has. Any of them can be missing: a feature that needs one shows a plain message instead of failing. */
export interface TillHardware {
  printer?: IPrinter;
  drawer?: ICashDrawer;
  scale?: IScale;
  kitchen?: IKitchenDisplay;
}

/** Everything a till feature needs, handed to it so features never reach for globals. */
export interface TillContext {
  staff: StaffRecord;
  language: LanguageCode;
  /** The shop's business pack: which features are on. */
  features: Features;
  api: Api;
  /** Which shop and till this is; null until the till is registered. */
  device: DeviceConfig | null;
  online: boolean;
  outbox: Outbox;
  catalogue: readonly CatalogueItem[];
  promotions: readonly PromotionDef[];
  hardware: TillHardware;
  /** Ask the sync runner to send the queue now (after something was written to it). */
  nudgeSync: () => void;
  /** The till's small local database: downloaded data and the till's own books (recent sales, cash in). Missing in tests that do not need it. */
  kv?: KeyValueStore;
  /** The shop's name for slips, when known. */
  shopName?: string;
  /** Put something in the sale that is being rung up on the Sell tab. */
  sale?: SaleBridge;
  /** Go back to the Sell tab. */
  openSell?: () => void;
}

/** What another tab can do to the sale in progress on the Sell tab (see SellScreen). */
export interface SaleBridge {
  /** A line at an agreed price (a ticket total, a service, a lay-by balance). The product must be one of the shop's products: the server checks. Not discounted. */
  addLine(line: { productId: string; name: string; priceCents: number; costCents?: number; quantity?: number }): Result<void>;
  /** One more of a catalogue product (or grams, for a product sold by weight), priced and discounted like a scanned item. */
  addProduct(productId: string, quantity?: number): Result<void>;
  /** Attach the sale to a customer (loyalty), or detach with null. */
  setCustomer(customerId: string | null): void;
}

/**
 * One tab of the till. Each feature lives in its own folder under ./features and exports one TillFeature; the shell finds them all, so adding a feature never means
 * editing a shared file. A feature with a flag only appears when the shop's pack has that feature on, and one that needs the server is greyed out offline.
 */
export interface TillFeature<K extends string = string> {
  id: string;
  order: number;
  title: K;
  messages: AreaMessages<K>;
  /** The pack feature that switches this tab on. Leave out for tabs every shop has. */
  flag?: FeatureFlag;
  /** The staff roles that see it. Leave out for everyone. */
  roles?: ReadonlyArray<StaffRecord["role"]>;
  requiresOnline?: boolean;
  component: ComponentType<{ ctx: TillContext }>;
}

export const defineFeature = <K extends string>(feature: TillFeature<K>): TillFeature<K> => feature;
