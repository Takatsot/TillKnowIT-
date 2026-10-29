import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Spending rules",
  loading: "Loading...",
  reload: "Try again",
  limitHeading: "Daily limit",
  limitMeans: "The most {name} can spend at the tuck shop in one day. When it is reached, the till says no until tomorrow.",
  spentToday: "Spent today: {amount}",
  limit: "Daily limit in rand",
  limitHint: "Leave empty for no limit.",
  notify: "Tell me when {name} buys something",
  lowAlert: "Warn me when the balance is below (rand)",
  lowAlertHint: "Leave empty for no warning.",
  amountBad: "Type an amount like 20 or 20,50.",
  saveLimits: "Save",
  limitsSaved: "Your rules were saved.",
  blockHeading: "Things {name} may not buy",
  blockMeans: "The tuck shop till refuses these, even when there is money. A change reaches the till the next time it syncs.",
  noBlocks: "Nothing is blocked.",
  category: "Whole group",
  product: "One item",
  categoryMeans: "Nothing in the group \"{key}\" can be bought.",
  productMeans: "The item \"{key}\" cannot be bought.",
  remove: "Allow again",
  removeLabel: "Allow {key} again",
  addHeading: "Block something",
  kind: "What do you want to block?",
  key: "Name or code",
  keyHint: "For example sweets, or the item code from the menu.",
  suggest: "From the school menu",
  block: "Block it",
  blocked: "Rules saved.",
} as const;

export type RulesKey = keyof typeof en;
export const messages: AreaMessages<RulesKey> = { en };
