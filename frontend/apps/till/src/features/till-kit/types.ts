/** The shapes the server sends and takes, copied from the C# contracts (ShopCore.Contracts, Bookings.Contracts, Customers.Contracts, Wallet.Contracts). */

export interface TicketLineDto {
  productId: string;
  name: string;
  quantity: number;
  priceCents: number;
  unitDivisor?: number;
  note?: string | null;
  addOnProductIds?: string[] | null;
}

export interface TicketResponse {
  id: string;
  label: string;
  tableId: string | null;
  status: string;
  lines: TicketLineDto[];
  totalCents: number;
  heldByDeviceId: string | null;
  heldUntil: string | null;
  lockedForYou: boolean;
}

export interface VoucherIssued {
  id: string;
  code: string;
  balanceCents: number;
  expiresOn: string | null;
}

export interface VoucherResponse {
  id: string;
  codeLast4: string;
  balanceCents: number;
  expiresOn: string | null;
  isVoid: boolean;
  isExpired: boolean;
}
