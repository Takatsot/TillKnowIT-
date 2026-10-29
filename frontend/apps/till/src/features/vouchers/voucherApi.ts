import type { Api } from "@tillknowit/api-client";
import { fail, newId, ok, type Result } from "@tillknowit/domain";
import type { Caller } from "../till-kit/kit";
import type { VoucherIssued, VoucherResponse } from "../till-kit/types";

/**
 * The calls behind the voucher tab, kept apart from the screen so the sell screen can pay with a voucher without copying them.
 * Routes: POST /shops/{shopId}/vouchers, /vouchers/check, /vouchers/redeem (SellingEndpoints.cs).
 */

export const sellVoucher = (api: Api, c: Caller, v: { amountCents: number; paidWith: "cash" | "card" | "qr"; reference: string; saleId?: string | null }): Promise<Result<VoucherIssued>> =>
  api.post<VoucherIssued>(`/shops/${c.shopId}/vouchers`, { deviceId: c.deviceId, staffId: c.staffId, amountCents: v.amountCents, paidWith: v.paidWith, reference: v.reference, saleId: v.saleId ?? null });

export const checkVoucher = (api: Api, c: Caller, code: string): Promise<Result<VoucherResponse>> =>
  api.post<VoucherResponse>(`/shops/${c.shopId}/vouchers/check`, { code: code.trim(), deviceId: c.deviceId, staffId: c.staffId });

export interface VoucherPayment {
  /** What goes on the sale as the "voucher" payment's reference: the sale then pays with method "voucher" for amountCents. */
  reference: string;
  amountCents: number;
  balanceCents: number;
}

/**
 * Takes money off a voucher. The reference makes it safe to repeat: pass the same one when retrying after a lost answer, and the server will not take the money
 * twice. Use `newRedemptionReference()` once per attempt.
 */
export async function redeemVoucher(api: Api, c: Caller, v: { code: string; amountCents: number; reference: string; saleId?: string | null }): Promise<Result<VoucherPayment>> {
  if (!Number.isInteger(v.amountCents) || v.amountCents <= 0) return fail("voucher.amount_invalid", "The amount must be more than zero.");
  const r = await api.post<VoucherResponse>(`/shops/${c.shopId}/vouchers/redeem`, {
    deviceId: c.deviceId, staffId: c.staffId, code: v.code.trim(), amountCents: v.amountCents, reference: v.reference, saleId: v.saleId ?? null,
  });
  return r.ok ? ok({ reference: v.reference, amountCents: v.amountCents, balanceCents: r.value.balanceCents }) : r;
}

/** Up to 80 characters; the server only needs it to be unique per redemption. */
export const newRedemptionReference = (): string => `vr-${newId()}`;
export const newIssueReference = (): string => `vi-${newId()}`;
