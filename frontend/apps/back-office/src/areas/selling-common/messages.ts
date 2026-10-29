import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  heading: "Which till is asking",
  hint: "Until sign-in exists, these routes want a till and a cashier. They start as this device and the signed-in person (stand-ins, not checked by us); change them if the server refuses.",
  deviceId: "Till (device) id",
  staffId: "Staff id",
  use: "Use these",
  missing: "Enter a till id and a staff id above to load this.",
  productsLoading: "Loading products...",
  productsEmpty: "No products yet.",
  chooseProduct: "Choose a product",
} as const;

export type CommonKey = keyof typeof en;
export const messages: AreaMessages<CommonKey> = { en };
