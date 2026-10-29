export interface Offer {
  position: number;
  supplierId: string;
  supplierName: string;
  supplierProductId: string;
  cases: number;
  unitsReceived: number;
  totalCostCents: number;
  trueCostPerUnitCents: number;
  marginPercent: number | null;
  isCheapest: boolean;
  isUsual: boolean;
  lastPriceCents: number | null;
  meetsMinimum: boolean;
  deliveryDaysAway: number;
  rating: number | null;
  dealTitle: string | null;
  label: string | null;
}

export interface Offers {
  productId: string;
  productName: string;
  quantityUnits: number;
  sellingPriceCents: number | null;
  offers: Offer[];
}

export interface OrderListItem {
  productId: string;
  productName: string;
  onHand: number;
  daysLeft: number | null;
  quantityUnits: number;
  flags: string[];
  offerCount: number;
  best: Offer | null;
  cheapest: Offer | null;
}

export interface OrderLine {
  id: string;
  name: string;
  orderedCases: number;
  confirmedCases: number | null;
  deliveredCases: number | null;
  effectiveCases: number;
  casePriceCents: number;
  status: string;
}

export interface Order {
  id: string;
  supplierName: string;
  reference: string;
  status: string;
  channel: string;
  paymentMethod: string;
  paymentStatus: string;
  lines: OrderLine[];
  totalCents: number;
  hasChanges: boolean;
  receivedAt: string | null;
}

export interface BuiltOrder {
  order: Order;
  warning: string | null;
}

export interface GroupDeal {
  id: string;
  supplierName: string;
  productName: string;
  unitsPerCase: number;
  casePriceCents: number;
  minTotalCases: number;
  totalCases: number;
  progressPercent: number;
  closesAt: string;
  status: string;
  yourCases: number | null;
}
