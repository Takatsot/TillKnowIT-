import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  loading: "Loading...",
  noChildren: "No child is linked to you yet. Open My children and add one with the code from the school.",
  which: "Which child?",
  reload: "Try again",
} as const;

export type SharedKey = keyof typeof en;
export const messages: AreaMessages<SharedKey> = { en };
