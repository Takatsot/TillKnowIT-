import type { Api } from "@tillknowit/api-client";
import { newId, type Result } from "@tillknowit/domain";
import type { Caller } from "../till-kit/kit";

/**
 * The calls behind the customers tab, kept apart from the screen so the sell screen can attach a customer to a sale and pay with points.
 * Routes: GET/POST /shops/{shopId}/customers, GET /customers/{id}/points, POST /customers/{id}/points/redeem (CustomersEndpoints.cs).
 */

export interface Customer {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  messageConsent: boolean;
  consentAt: string | null;
  points: number;
}

export interface PointsEntry {
  type: string;
  points: number;
  note: string | null;
  at: string;
  saleId: string | null;
}

export interface Points {
  customerId: string;
  points: number;
  valueCents: number;
  minRedeemPoints: number;
  recent: PointsEntry[];
}

export interface PointsRedemption {
  /** The reference to put on the sale's "voucher" payment of valueCents. */
  redemptionReference: string;
  points: number;
  valueCents: number;
  balanceAfter: number;
}

export const findCustomers = (api: Api, c: Caller, search: string): Promise<Result<Customer[]>> =>
  api.get<Customer[]>(`/shops/${c.shopId}/customers`, { search: search.trim(), deviceId: c.deviceId, staffId: c.staffId });

export const addCustomer = (api: Api, c: Caller, v: { id: string; name: string; phone: string; email: string | null; messageConsent: boolean }): Promise<Result<Customer>> =>
  api.post<Customer>(`/shops/${c.shopId}/customers`, { deviceId: c.deviceId, staffId: c.staffId, ...v, consentSource: v.messageConsent ? "till" : null });

export const getPoints = (api: Api, c: Caller, customerId: string): Promise<Result<Points>> =>
  api.get<Points>(`/shops/${c.shopId}/customers/${customerId}/points`, { deviceId: c.deviceId, staffId: c.staffId });

/** Repeating the same reference gives back the same redemption, so a retry after a lost answer never takes the points twice. */
export const redeemPoints = (api: Api, c: Caller, customerId: string, points: number, reference: string): Promise<Result<PointsRedemption>> =>
  api.post<PointsRedemption>(`/shops/${c.shopId}/customers/${customerId}/points/redeem`, { deviceId: c.deviceId, staffId: c.staffId, points, reference });

export const newPointsReference = (): string => `pr-${newId()}`;
