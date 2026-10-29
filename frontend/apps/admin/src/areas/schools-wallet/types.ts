import type { Result } from "@tillknowit/domain";

/** Shapes of Wallet.Contracts, as the server sends them (camelCase). */
export interface School {
  id: string;
  name: string;
  emisNumber: string | null;
  shopId: string;
  status: "pending" | "verified" | "suspended" | string;
  payoutSchedule: "daily" | "weekly" | "monthly" | string;
  feeBasisPoints: number;
  staffAccountsEnabled: boolean;
  dailyCeilingCents: number | null;
  offlineSplitMinutes: number;
}

export interface Holder {
  id: string;
  schoolId: string;
  type: string;
  firstName: string;
  lastName: string;
  grade: string | null;
  className: string | null;
  status: string;
  walletId: string;
  hasPhoto: boolean;
}

export interface HolderWithCode {
  holder: Holder;
  linkCode: string;
}

export interface ImportResult {
  created: number;
  duplicates: number;
  invalid: number;
  rows: { row: number; name: string; outcome: string; detail: string | null }[];
  linkCodes: { holderId: string; name: string; grade: string | null; className: string | null; linkCode: string }[];
}

export interface Break {
  code: string;
  name: string;
  cutOff: string;
}

export interface MenuItem {
  id: string;
  name: string;
  priceCents: number;
  productId: string | null;
  categoryCode: string | null;
  isAvailable: boolean;
}

export interface Menu {
  breaks: Break[];
  items: MenuItem[];
}

export interface StaffMonth {
  month: string;
  totalOwedCents: number;
  teachers: { holderId: string; name: string; owedCents: number }[];
}

export interface Payout {
  id: string;
  shopId: string;
  amountCents: number;
  status: string;
  day: string;
  error: string | null;
}

export interface RunPayouts {
  blocked: boolean;
  blockedReason: string | null;
  payouts: Payout[];
}

export interface ShopSales {
  salesCents: number;
  feesCents: number;
  paidOutCents: number;
  owedToShopCents: number;
  payouts: Payout[];
}

export interface Reconciliation {
  id: string;
  day: string;
  partnerTotalCents: number;
  ledgerTotalCents: number;
  differenceCents: number;
  status: "matched" | "mismatch" | "resolved" | string;
  note: string | null;
}

/** Sends the holders import. Defaults to Api.upload; a test passes its own. */
export type HolderUploader = (schoolId: string, callerPersonId: string, file: File) => Promise<Result<ImportResult>>;
