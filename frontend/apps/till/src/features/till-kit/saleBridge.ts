import type { Result } from "@tillknowit/domain";
import type { TillContext } from "../../feature";
import { costOf, divisorOf, lineCents } from "./LinesEditor";
import type { TicketLineDto } from "./types";

/**
 * Puts goods from a quote or ticket onto the sale being rung up on the Sell tab, at the prices they were given.
 * A weighed line goes in as one item at its total, because its quantity is grams. Stops at the first line the sale refuses.
 */
export function addLinesToSale(ctx: TillContext, lines: readonly TicketLineDto[]): { ok: true } | { ok: false; name: string; message: string } {
  const sale = ctx.sale;
  if (!sale) return { ok: false, name: "", message: "" };
  for (const l of lines) {
    const costCents = costOf(ctx.catalogue, l.productId);
    const r: Result<void> = sale.addLine(divisorOf(l) === 1000
      ? { productId: l.productId, name: l.name, priceCents: lineCents(l), costCents, quantity: 1 }
      : { productId: l.productId, name: l.name, priceCents: l.priceCents, costCents, quantity: l.quantity });
    if (!r.ok) return { ok: false, name: l.name, message: r.message };
  }
  return { ok: true };
}
