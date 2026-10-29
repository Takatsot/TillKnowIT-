import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  heading: "Which till is asking",
  hint: "These routes are asked on behalf of a till and a staff member. Choose an approved till; you act as your own staff membership at this shop.",
  loading: "Loading tills and staff...",
  device: "Till",
  staff: "Staff member",
  actingAs: "You are acting as yourself (staff id {id}).",
  noDevice: "This shop has no approved till yet. Register and approve one under Staff and tills, then come back.",
  noMembership: "You have no staff membership at this shop, so choose a staff member below.",
  noStaff: "There is no staff member to act as. Add one under Staff and tills.",
  loadFailed: "Could not load the tills and staff: {message}",
  missing: "Choose a till and a staff member above to load this.",
  productsLoading: "Loading products...",
  productsEmpty: "No products yet.",
  chooseProduct: "Choose a product",
} as const;

export type CommonKey = keyof typeof en;
export const messages: AreaMessages<CommonKey> = { en };
