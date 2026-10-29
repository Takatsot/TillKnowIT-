import { buildKitchenTicket, type KitchenOrder } from "@tillknowit/hardware";
import type { TillHardware } from "../../feature";

export interface KitchenOrderView {
  id: string;
  orderNo: string;
  tableLabel: string | null;
  items: Array<{ name: string; quantity: number; note?: string | null }>;
  createdAt: string;
}

export const toHardwareOrder = (o: KitchenOrderView): KitchenOrder => ({
  id: o.id,
  table: o.tableLabel ?? undefined,
  lines: o.items.map((i) => ({ quantity: i.quantity, name: i.name, modifiers: [], note: i.note ?? undefined })),
  sentAt: new Date(o.createdAt),
});

export type PrintOutcome = { kind: "none" } | { kind: "done" } | { kind: "failed"; reason: string };

/** Puts the order on the kitchen screen when there is one, otherwise on the printer. Never throws: the order is already on the server. */
export async function printKitchenOrder(hardware: TillHardware, order: KitchenOrderView, only?: "printer"): Promise<PrintOutcome> {
  const hw = toHardwareOrder(order);
  try {
    if (hardware.kitchen && only !== "printer") {
      await hardware.kitchen.send(hw);
      return { kind: "done" };
    }
    if (hardware.printer) {
      await hardware.printer.print(buildKitchenTicket(hw));
      return { kind: "done" };
    }
    return { kind: "none" };
  } catch (e) {
    return { kind: "failed", reason: e instanceof Error ? e.message : "The device did not answer." };
  }
}
