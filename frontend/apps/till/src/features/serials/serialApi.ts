import type { Api } from "@tillknowit/api-client";
import type { Result } from "@tillknowit/domain";
import type { Caller } from "../till-kit/kit";

/**
 * Serial numbers. Routes: POST /shops/{shopId}/sales/{saleId}/serials and GET /shops/{shopId}/serials?text= (SellingEndpoints.cs).
 * The sell screen can call `recordSerials` right after a sale has reached the server instead of sending the cashier to the tab.
 */

export interface SerialItem {
  productId: string;
  serial: string;
}

export interface SerialRow {
  productId: string;
  productName: string;
  serial: string;
  saleId: string;
  soldAt: string;
}

export const recordSerials = (api: Api, c: Caller, saleId: string, items: readonly SerialItem[]): Promise<Result<void>> =>
  api.post<void>(`/shops/${c.shopId}/sales/${saleId}/serials`, { deviceId: c.deviceId, staffId: c.staffId, items });

export const findSerials = (api: Api, c: Caller, text: string): Promise<Result<SerialRow[]>> =>
  api.get<SerialRow[]>(`/shops/${c.shopId}/serials`, { text: text.trim(), deviceId: c.deviceId, staffId: c.staffId });
