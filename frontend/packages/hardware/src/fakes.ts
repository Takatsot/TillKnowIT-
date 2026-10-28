import { HardwareError, type DeviceStatus, type ICashDrawer, type IKitchenDisplay, type IPrinter, type IScale, type KitchenOrder, type ScaleReading } from "./ports";

/** A printer that keeps what it was sent, for tests and for running the till with no printer attached. */
export class FakePrinter implements IPrinter {
  readonly jobs: Uint8Array[] = [];
  state: DeviceStatus = "ready";

  async print(job: Uint8Array): Promise<void> {
    if (this.state === "offline") throw new HardwareError("printer_offline", "The printer is not connected.");
    if (this.state === "out_of_paper") throw new HardwareError("out_of_paper", "The printer is out of paper.");
    if (this.state === "cover_open") throw new HardwareError("cover_open", "The printer cover is open.");
    this.jobs.push(job);
  }

  async status(): Promise<DeviceStatus> {
    return this.state;
  }
}

export class FakeCashDrawer implements ICashDrawer {
  opened = 0;
  failing = false;

  async open(): Promise<void> {
    if (this.failing) throw new HardwareError("drawer_failed", "The cash drawer did not open.");
    this.opened++;
  }
}

/** A scale that plays back a list of readings, then repeats the last one. */
export class FakeScale implements IScale {
  private index = 0;
  offline = false;

  constructor(private readonly readings: readonly ScaleReading[]) {}

  async read(): Promise<ScaleReading> {
    if (this.offline) throw new Error("no answer");
    const reading = this.readings[Math.min(this.index, this.readings.length - 1)] ?? { grams: 0, stable: true };
    this.index++;
    return reading;
  }
}

export class FakeKitchenDisplay implements IKitchenDisplay {
  readonly orders: KitchenOrder[] = [];
  reachable = true;

  async send(order: KitchenOrder): Promise<void> {
    if (!this.reachable) throw new HardwareError("kitchen_unreachable", "The kitchen screen cannot be reached.");
    this.orders.push(order);
  }
}
