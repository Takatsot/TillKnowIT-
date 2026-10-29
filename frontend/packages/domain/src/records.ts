import { businessDayOf } from "./cashup";
import { checkCreditEntry, type CreditEntryType } from "./credit";
import type { Cents } from "./money";
import { queueRecord, type Outbox, type RecordKind } from "./outbox";
import { fail, ok, type Result } from "./result";
import { checkMovement, type StockMovementDraft } from "./stock";

/**
 * The records a till uploads besides sales, in the JSON the server's upload takes (the *Upload records in ShopCore.Contracts/SyncUpload.cs). Each builder runs the
 * same checks as the server, with the same codes, so a record that is built here is not refused for those reasons. Ids are made on the till (newId).
 */

export interface StockMovementDto {
  id: string;
  staffId: string;
  productId: string;
  type: string;
  quantity: number;
  createdAt: string;
  saleId: string | null;
  unitCostCents: Cents | null;
  reasonCode: string | null;
  supplierName: string | null;
  invoiceRef: string | null;
}

export interface CancellationDto {
  id: string;
  staffId: string;
  saleId: string;
  reason: string;
  cancelledAt: string;
}

export interface RefundDto {
  id: string;
  staffId: string;
  saleId: string;
  reason: string;
  /** How the customer is paid back; null means the way the sale was paid. */
  methodCode: string | null;
  /** The manager who approved a refund above the shop's threshold. */
  approvedBy: string | null;
  lines: Array<{ productId: string; quantity: number }>;
  refundedAt: string;
}

export interface CreditEntryDto {
  id: string;
  staffId: string;
  accountId: string;
  type: CreditEntryType;
  amountCents: Cents;
  saleId: string | null;
  paymentMethodCode: string | null;
  createdAt: string;
}

export interface CashUpDto {
  id: string;
  staffId: string;
  /** yyyy-mm-dd, the South African business day. */
  day: string;
  floatCents: Cents;
  expectedCents: Cents;
  countedCents: Cents;
  note: string | null;
  cashedUpAt: string;
}

export const MAX_REASON_LENGTH = 200;

export interface RecordContext {
  id: string;
  staffId: string;
  at: Date;
}

export function stockMovementRecord(draft: StockMovementDraft & { supplierName?: string | null; invoiceRef?: string | null }, ctx: RecordContext): Result<StockMovementDto> {
  const checked = checkMovement(draft);
  if (!checked.ok) return checked;
  return ok({
    id: ctx.id, staffId: ctx.staffId, productId: draft.productId, type: draft.type, quantity: draft.quantity, createdAt: ctx.at.toISOString(),
    saleId: draft.saleId ?? null, unitCostCents: draft.unitCostCents ?? null, reasonCode: draft.reasonCode?.trim() || null,
    supplierName: draft.supplierName?.trim() || null, invoiceRef: draft.invoiceRef?.trim() || null,
  });
}

export function cancellationRecord(input: { saleId: string; reason: string }, ctx: RecordContext): Result<CancellationDto> {
  const reason = input.reason.trim();
  if (reason === "" || reason.length > MAX_REASON_LENGTH) return fail("cancellation.reason_required", "Say why the sale is being cancelled.");
  return ok({ id: ctx.id, staffId: ctx.staffId, saleId: input.saleId, reason, cancelledAt: ctx.at.toISOString() });
}

export function refundRecord(
  input: { saleId: string; reason: string; methodCode?: string | null; approvedBy?: string | null; lines: ReadonlyArray<{ productId: string; quantity: number }> },
  ctx: RecordContext,
): Result<RefundDto> {
  const reason = input.reason.trim();
  if (reason === "" || reason.length > MAX_REASON_LENGTH) return fail("refund.reason_required", "Say why the goods are being taken back.");
  const lines = input.lines.filter((l) => l.quantity > 0);
  if (lines.length === 0) return fail("refund.no_lines", "Choose what is being refunded.");
  if (lines.some((l) => !Number.isInteger(l.quantity))) return fail("refund.quantity_invalid", "The quantity must be a whole number.");
  if (new Set(lines.map((l) => l.productId)).size !== lines.length) return fail("refund.line_repeated", "List each item once, with its quantity.");
  return ok({
    id: ctx.id, staffId: ctx.staffId, saleId: input.saleId, reason, methodCode: input.methodCode ?? null, approvedBy: input.approvedBy ?? null,
    lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity })), refundedAt: ctx.at.toISOString(),
  });
}

/** The credit entry a credit sale needs (the server does not make it): a positive "sale" entry for the sale's total. */
export function creditSaleRecord(input: { accountId: string; saleId: string; totalCents: Cents }, ctx: RecordContext): Result<CreditEntryDto> {
  return creditEntryRecord({ type: "sale", amountCents: input.totalCents, saleId: input.saleId, accountId: input.accountId, paymentMethodCode: null }, ctx);
}

/** A customer pays off credit: the amount is given as a positive number and stored negative. */
export function creditPaymentRecord(input: { accountId: string; amountCents: Cents; paymentMethodCode: string }, ctx: RecordContext): Result<CreditEntryDto> {
  if (!Number.isInteger(input.amountCents) || input.amountCents <= 0) return fail("credit.amount_zero", "A payment must be more than zero.");
  return creditEntryRecord({ type: "payment", amountCents: -input.amountCents, saleId: null, accountId: input.accountId, paymentMethodCode: input.paymentMethodCode }, ctx);
}

/** The entry that undoes a credit sale when the sale is cancelled. */
export function creditCancelRecord(input: { accountId: string; saleId: string; totalCents: Cents }, ctx: RecordContext): Result<CreditEntryDto> {
  return creditEntryRecord({ type: "cancel", amountCents: -input.totalCents, saleId: input.saleId, accountId: input.accountId, paymentMethodCode: null }, ctx);
}

function creditEntryRecord(e: { type: CreditEntryType; amountCents: Cents; saleId: string | null; accountId: string; paymentMethodCode: string | null }, ctx: RecordContext): Result<CreditEntryDto> {
  const checked = checkCreditEntry({ type: e.type, amountCents: e.amountCents, saleId: e.saleId, paymentMethod: e.paymentMethodCode });
  if (!checked.ok) return checked;
  return ok({ id: ctx.id, staffId: ctx.staffId, accountId: e.accountId, type: e.type, amountCents: e.amountCents, saleId: e.saleId, paymentMethodCode: e.paymentMethodCode, createdAt: ctx.at.toISOString() });
}

export function cashUpRecord(input: { floatCents: Cents; expectedCents: Cents; countedCents: Cents; note?: string | null }, ctx: RecordContext): Result<CashUpDto> {
  if (![input.floatCents, input.expectedCents, input.countedCents].every((c) => Number.isInteger(c) && c >= 0)) return fail("cashup.amount_invalid", "Amounts cannot be negative.");
  const note = input.note?.trim() || null;
  if ((note?.length ?? 0) > 500) return fail("cashup.note_too_long", "The note is too long.");
  return ok({
    id: ctx.id, staffId: ctx.staffId, day: businessDayOf(ctx.at), floatCents: input.floatCents, expectedCents: input.expectedCents, countedCents: input.countedCents,
    note, cashedUpAt: ctx.at.toISOString(),
  });
}

/** Queues a record built above, once it has passed its checks. */
export async function queueBuilt<T extends { id: string }>(outbox: Outbox, kind: RecordKind, built: Result<T>, now: Date): Promise<Result<T>> {
  if (!built.ok) return built;
  await queueRecord(outbox, kind, built.value.id, built.value, now);
  return built;
}
