import { fail, ok, type Result } from "./result";

export type StockMovementType = "receive" | "sale" | "cancel" | "count" | "write_off";

export interface StockMovementDraft {
  productId: string;
  type: StockMovementType;
  /** Signed: positive adds stock, negative removes it. */
  quantity: number;
  saleId?: string | null;
  reasonCode?: string | null;
  unitCostCents?: number | null;
}

/** Same rules as StockMovement.Record in C#, with the same error codes. */
export function checkMovement(m: StockMovementDraft): Result<StockMovementDraft> {
  if (m.quantity === 0) return fail("stock.quantity_zero", "A stock movement cannot be zero.");

  const signOk =
    m.type === "receive" || m.type === "cancel"
      ? m.quantity > 0
      : m.type === "sale" || m.type === "write_off"
        ? m.quantity < 0
        : true;
  if (!signOk) return fail("stock.quantity_sign", `A ${m.type} movement has the wrong sign.`);

  if ((m.type === "count" || m.type === "write_off") && (m.reasonCode ?? "").trim() === "")
    return fail("stock.reason_required", "Counts and write-offs need a reason.");
  if ((m.type === "sale" || m.type === "cancel") && !m.saleId)
    return fail("stock.sale_required", "Sale and cancel movements must point at their sale.");
  if ((m.unitCostCents ?? 0) < 0) return fail("stock.cost_invalid", "Cost cannot be negative.");
  return ok(m);
}

/** Stock on hand per product. Recalculated from movements after every sync; it may go negative. */
export function onHand(movements: readonly { productId: string; quantity: number }[]): Map<string, number> {
  const stock = new Map<string, number>();
  for (const m of movements) stock.set(m.productId, (stock.get(m.productId) ?? 0) + m.quantity);
  return stock;
}

/** Negative stock is allowed so a sale is never blocked, but it flags a count. */
export const needsCount = (onHandQuantity: number): boolean => onHandQuantity < 0;

/** The movements that undo a sale: stock goes back, one cancel movement per line. */
export function cancellationMovements(
  saleId: string,
  lines: readonly { productId: string; quantity: number }[],
): StockMovementDraft[] {
  return lines.map((l) => ({ productId: l.productId, type: "cancel", quantity: l.quantity, saleId }));
}
