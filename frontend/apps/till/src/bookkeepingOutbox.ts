import { isSaleEntry, type CreditEntryDto, type Outbox, type OutboxEntry, type RefundDto, type CancellationDto } from "@tillknowit/domain";
import { addRefunded, logCash, logSale, markCancelled, recentSales, refundAmount } from "./ledger";
import type { KeyValueStore } from "./localData";

/**
 * Wraps the upload queue so the till's own books (see ledger.ts) follow whatever is queued, whichever tab queued it: a sale rung up on the Sell tab or a ticket tab,
 * a cancellation, a refund, a credit payment. The cash-up, the list of recent sales and what can still be refunded all come from these books, so they work with no
 * connection. Queuing the same entry twice changes nothing, here or in the queue.
 */
export class BookkeepingOutbox implements Outbox {
  constructor(private readonly inner: Outbox, private readonly kv: KeyValueStore) {}

  async add(entry: OutboxEntry): Promise<void> {
    const already = (await this.inner.all()).some((e) => e.id === entry.id);
    await this.inner.add(entry);
    if (already) return;
    try {
      await this.book(entry);
    } catch {
      // The books are a convenience; a failure here must never undo or hide a queued sale.
    }
  }

  all(): Promise<OutboxEntry[]> {
    return this.inner.all();
  }

  update(entry: OutboxEntry): Promise<void> {
    return this.inner.update(entry);
  }

  remove(id: string): Promise<void> {
    return this.inner.remove(id);
  }

  private async book(entry: OutboxEntry): Promise<void> {
    if (isSaleEntry(entry)) return logSale(this.kv, entry.sale);
    if (entry.kind === "cancellation") return markCancelled(this.kv, (entry.record as CancellationDto).saleId);
    if (entry.kind === "refund") {
      const refund = entry.record as RefundDto;
      await addRefunded(this.kv, refund.saleId, refund.lines);
      const sale = (await recentSales(this.kv)).find((s) => s.id === refund.saleId);
      // A refund goes back the way the sale was paid unless it says otherwise; only cash leaves the drawer.
      const method = refund.methodCode ?? (sale && sale.cashCents > 0 && sale.paymentMethodCode === "cash" ? "cash" : sale?.paymentMethodCode);
      if (sale && method === "cash") await logCash(this.kv, { at: refund.refundedAt, cents: -refundAmount(sale, refund.lines), kind: "refund" });
      return;
    }
    if (entry.kind === "credit_entry") {
      const credit = entry.record as CreditEntryDto;
      if (credit.type === "payment" && credit.paymentMethodCode === "cash") await logCash(this.kv, { at: credit.createdAt, cents: -credit.amountCents, kind: "credit_payment" });
    }
  }
}
