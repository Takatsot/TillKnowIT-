import { vatIncluded, type CatalogueItem, type SaleUploadDto } from "@tillknowit/domain";
import { costOf, divisorOf, linesTotal } from "../till-kit/LinesEditor";
import type { TicketLineDto } from "../till-kit/types";

/**
 * The sale that rings up paid-off goods: an ordinary sale whose payment method is "layby" (the money was taken in the lay-by payments), at the prices agreed
 * when the lay-by began. The server matches it to the lay-by by method and total.
 */
export function laybyCollectSale(lines: readonly TicketLineDto[], catalogue: readonly CatalogueItem[], ids: { saleId: string; staffId: string }, now: Date, vatRatePercent = 15): SaleUploadDto {
  const total = linesTotal(lines);
  return {
    id: ids.saleId, staffId: ids.staffId, soldAt: now.toISOString(), paymentMethodCode: "layby", totalCents: total, vatCents: vatIncluded(total, vatRatePercent),
    tenderedCents: null, changeCents: null, accountId: null,
    lines: lines.map((l) => ({
      productId: l.productId, name: l.name, quantity: l.quantity, priceCents: l.priceCents, costCents: costOf(catalogue, l.productId), unitDivisor: divisorOf(l), discountCents: 0, promotionId: null,
    })),
    payments: null, tipCents: 0, customerId: null,
  };
}
