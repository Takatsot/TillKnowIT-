import type { Api } from "@tillknowit/api-client";
import type { Result } from "@tillknowit/domain";
import type { Caller } from "../till-kit/kit";

/**
 * School tuckshop wallet payment: POST /shops/{shopId}/wallet-payments (Wallet.Infrastructure/WalletEndpoints.cs, WalletPaymentRequest in WalletContracts.cs).
 * The sale id makes it safe to repeat: sending the same one again gives back the first answer with `repeated: true` and takes nothing more. The purchase id in the
 * answer is the reference of the sale's "wallet" payment.
 */

export interface WalletItem {
  productId: string | null;
  categoryCode: string | null;
  name: string;
  quantity: number;
  priceCents: number;
}

export interface WalletPaymentResult {
  purchaseId: string;
  holderId: string;
  holderName: string;
  grade: string | null;
  className: string | null;
  hasPhoto: boolean;
  amountCents: number;
  /** Null for a staff account, which has no balance. */
  balanceCents: number | null;
  repeated: boolean;
}

export const takeWalletPayment = (
  api: Api,
  c: Caller,
  p: { saleId: string; amountCents: number; items: readonly WalletItem[]; token: string; kind: "qr" | "card"; useStaffAccount?: boolean },
): Promise<Result<WalletPaymentResult>> =>
  api.post<WalletPaymentResult>(`/shops/${c.shopId}/wallet-payments`, {
    deviceId: c.deviceId, staffId: c.staffId, saleId: p.saleId, token: p.kind === "card" ? p.token.trim() : null, qrToken: p.kind === "qr" ? p.token.trim() : null,
    amountCents: p.amountCents, items: p.items, useStaffAccount: p.useStaffAccount ?? false,
  });
