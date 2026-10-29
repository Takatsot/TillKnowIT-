/** Shapes the wallet server sends (backend/src/Modules/Wallet/Wallet.Contracts). Money is whole cents. */
export interface Child {
  holderId: string;
  walletId: string;
  name: string;
  grade: string | null;
  className: string | null;
  school: string;
  balanceCents: number;
  dailyLimitCents: number | null;
  walletStatus: string;
}

export interface Restriction {
  kind: string;
  key: string;
}

export interface Wallet {
  id: string;
  holderId: string;
  holderName: string;
  balanceCents: number;
  dailyLimitCents: number | null;
  spentTodayCents: number;
  status: string;
  restrictions: Restriction[];
  notifyOnPurchase: boolean;
  lowBalanceAlertCents: number | null;
}

export interface StatementLine {
  at: string;
  kind: string;
  description: string;
  amountCents: number;
  balanceAfterCents: number;
}

export interface TopUp {
  id: string;
  walletId: string;
  amountCents: number;
  status: string;
  checkoutUrl: string | null;
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

export interface PreOrderLine {
  menuItemId: string;
  name: string;
  quantity: number;
  priceCents: number;
}

export interface PreOrder {
  id: string;
  holderId: string;
  breakCode: string;
  collectOn: string;
  status: string;
  totalCents: number;
  cutOffAt: string;
  lines: PreOrderLine[];
}

export interface Token {
  id: string;
  holderId: string;
  type: string;
  status: string;
}
