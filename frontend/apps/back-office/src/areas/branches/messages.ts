import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Branches",
  heading: "Your shops",
  add: "Add a branch",
  name: "Branch name",
  area: "Municipality code (optional)",
  areaHint: "Leave empty to use the head shop's area.",
  create: "Add branch",
  head: "Head shop",
  branch: "Branch",
  status: "Status",
  colName: "Shop",
  colKind: "Kind",
  colArea: "Area",
  empty: "No branches yet.",
  added: "{name} was added.",
  planNote: "Branches are part of the Pro plan.",
  loading: "Loading...",
} as const;

export type BranchKey = keyof typeof en;
export const messages: AreaMessages<BranchKey> = { en };
