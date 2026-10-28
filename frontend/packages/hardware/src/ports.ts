/**
 * The till talks to hardware only through these ports. The real adapters (Web Serial, WebUSB, Bluetooth, a print service) are written per device once the devices
 * are chosen; until then the till runs against the fakes in ./fakes, and everything that decides what to print or what a weight means is tested.
 */

export type DeviceStatus = "ready" | "busy" | "offline" | "out_of_paper" | "cover_open";

export interface IPrinter {
  /** Raw ESC/POS bytes, as built by EscPos. Resolves once the printer has accepted the job; rejects if it cannot. */
  print(job: Uint8Array): Promise<void>;
  status(): Promise<DeviceStatus>;
}

export interface ICashDrawer {
  open(): Promise<void>;
}

/** A reading from a scale. Stable is true when the weight has stopped moving: only a stable weight may be sold. */
export interface ScaleReading {
  grams: number;
  stable: boolean;
}

export interface IScale {
  read(): Promise<ScaleReading>;
}

/** One order line for the kitchen: what to make, for which table or ticket, with any add-ons. */
export interface KitchenLine {
  quantity: number;
  name: string;
  modifiers: readonly string[];
  note?: string;
}

export interface KitchenOrder {
  id: string;
  table?: string;
  lines: readonly KitchenLine[];
  sentAt: Date;
}

export interface IKitchenDisplay {
  send(order: KitchenOrder): Promise<void>;
}

/** What went wrong, in words the cashier can act on. Never the device's own error text. */
export class HardwareError extends Error {
  constructor(readonly code: "printer_offline" | "out_of_paper" | "cover_open" | "scale_unstable" | "scale_offline" | "drawer_failed" | "kitchen_unreachable", message: string) {
    super(message);
    this.name = "HardwareError";
  }
}
