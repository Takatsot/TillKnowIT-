import type { Api } from "@tillknowit/api-client";
import type { CatalogueItem, Outbox, PromotionDef } from "@tillknowit/domain";
import type { ICashDrawer, IKitchenDisplay, IPrinter, IScale } from "@tillknowit/hardware";
import type { AreaMessages, LanguageCode } from "@tillknowit/i18n";
import type { FeatureFlag, Features } from "@tillknowit/packs";
import type { ComponentType } from "react";
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
