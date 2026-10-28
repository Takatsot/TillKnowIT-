import { EscPos, PAPER_COLUMNS } from "./escpos";
import type { KitchenOrder } from "./ports";

export interface SlipLine {
  name: string;
  quantity: number;
  totalCents: number;
}

export interface SlipData {
  shopName: string;
  lines: readonly SlipLine[];
  totalCents: number;
  vatCents: number;
  paymentMethod: string;
  soldAt: Date;
  /** The link to the slip online. When present it is printed as a QR code so the customer can keep it on their phone. */
  slipUrl?: string;
  /** Printed under the total, for example a thank-you or the shop's VAT number. */
  footer?: string;
}

/** Plain ASCII, so it prints on any code page: R12.50, never a currency symbol with a special space. */
export const rand = (cents: number): string => `R${(cents / 100).toFixed(2)}`;

const pad = (left: string, right: string, width: number): string => {
  const gap = width - left.length - right.length;
  return gap >= 1 ? left + " ".repeat(gap) + right : left.slice(0, Math.max(0, width - right.length - 1)) + " " + right;
};

/** The date and time as people at the till read it: South African time. */
export function saDateTime(at: Date): string {
  const local = new Date(at.getTime() + 2 * 60 * 60_000);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${local.getUTCFullYear()}-${p(local.getUTCMonth() + 1)}-${p(local.getUTCDate())} ${p(local.getUTCHours())}:${p(local.getUTCMinutes())}`;
}

/** A customer slip: shop, lines, total with the VAT in it, how it was paid, and a QR code to the online slip. */
export function buildSlip(slip: SlipData, columns: number = PAPER_COLUMNS.mm80, options: { openDrawer?: boolean } = {}): Uint8Array {
  const job = new EscPos();
  if (options.openDrawer) job.openDrawer();
  job.align("center").bold(true).line(slip.shopName.slice(0, columns)).bold(false).line(saDateTime(slip.soldAt)).line("-".repeat(columns)).align("left");

  for (const l of slip.lines) {
    const qty = l.quantity === 1 ? "" : `${l.quantity} x `;
    job.line(pad(`${qty}${l.name}`, rand(l.totalCents), columns));
  }

  job.line("-".repeat(columns)).bold(true).line(pad("TOTAL", rand(slip.totalCents), columns)).bold(false);
  if (slip.vatCents > 0) job.line(pad("VAT included", rand(slip.vatCents), columns));
  job.line(pad("Paid by", slip.paymentMethod, columns));
  if (slip.footer) job.feed(1).align("center").line(slip.footer.slice(0, columns));
  if (slip.slipUrl) job.feed(1).align("center").qr(slip.slipUrl).feed(1);
  return job.cut().build();
}

/** A kitchen ticket: big table name, then every line with its add-ons indented, so it can be read from across the kitchen. */
export function buildKitchenTicket(order: KitchenOrder, columns: number = PAPER_COLUMNS.mm80): Uint8Array {
  const job = new EscPos().align("center").large(true).bold(true).line(order.table ? `TABLE ${order.table}` : "TAKEAWAY").large(false).bold(false).line(saDateTime(order.sentAt)).line("=".repeat(columns)).align("left");
  for (const l of order.lines) {
    job.bold(true).line(`${l.quantity} x ${l.name}`).bold(false);
    for (const m of l.modifiers) job.line(`   + ${m}`);
    if (l.note) job.line(`   NOTE: ${l.note}`);
  }
  return job.cut().build();
}
