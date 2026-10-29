import type { AreaMessages } from "@tillknowit/i18n";
import type { ComponentType } from "react";

/**
 * One screen area of an app (Branches, Billing...). Each area exports one Section from its own folder; the app shell finds them all, so adding an area never means
 * editing a shared file. A section is only shown when the session has what it needs.
 */
export interface Section<K extends string = string> {
  id: string;
  /** Where it appears in the menu: lower first. */
  order: number;
  /** The menu title key in the section's own messages. */
  title: K;
  messages: AreaMessages<K>;
  /** What the session must have for this section to make sense. */
  needs?: ReadonlyArray<"personId" | "shopId" | "supplierOrganisationId" | "deviceId">;
  component: ComponentType;
}

export function defineSection<K extends string>(section: Section<K>): Section<K> {
  return section;
}
