import type { CatalogueItem, PromotionDef } from "@tillknowit/domain";

/**
 * The products and promotions this till can sell. Later this reads the synced products table. Until sync exists, development builds get a few example items
 * and production builds get none, so a real shop never sells example products by accident.
 */
export async function loadCatalogue(): Promise<CatalogueItem[]> {
  if (!import.meta.env.DEV) return [];
  return [
    { id: "dev-bread", name: "Bread", barcode: "6001000000011", priceCents: 1800, costCents: 1200, soldByWeight: false },
    { id: "dev-milk", name: "Milk 1L", barcode: "6001000000028", priceCents: 2199, costCents: 1700, soldByWeight: false },
    { id: "dev-cooldrink", name: "Cooldrink 330ml", barcode: "6001000000035", priceCents: 1500, costCents: 1000, soldByWeight: false },
    { id: "dev-mince", name: "Beef mince", barcode: "123456", priceCents: 12_000, costCents: 8_000, soldByWeight: true },
  ];
}

export async function loadPromotions(): Promise<PromotionDef[]> {
  return [];
}
