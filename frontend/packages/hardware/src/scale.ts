import { HardwareError, type IScale } from "./ports";

export interface WeighOptions {
  /** How many times to ask before giving up. */
  attempts?: number;
  /** Two readings this close (in grams) count as the same weight. */
  toleranceGrams?: number;
  /** Waits between readings. Injected so tests do not really wait. */
  delay?: (ms: number) => Promise<void>;
  intervalMs?: number;
}

/**
 * Reads a weight the cashier can sell: stable, above zero, and the same on two readings in a row. A scale that is still settling, or a bowl being
 * touched, never sells a wrong weight; the cashier is told to wait instead.
 */
export async function readStableWeight(scale: IScale, options: WeighOptions = {}): Promise<number> {
  const attempts = options.attempts ?? 10;
  const tolerance = options.toleranceGrams ?? 2;
  const delay = options.delay ?? ((ms: number) => new Promise<void>((r) => setTimeout(r, ms)));
  let previous: number | null = null;

  for (let i = 0; i < attempts; i++) {
    let reading;
    try {
      reading = await scale.read();
    } catch {
      throw new HardwareError("scale_offline", "The scale is not answering. Check it is on and connected.");
    }
    if (reading.stable && reading.grams > 0) {
      if (previous !== null && Math.abs(previous - reading.grams) <= tolerance) return reading.grams;
      previous = reading.grams;
    } else previous = null;
    await delay(options.intervalMs ?? 150);
  }
  throw new HardwareError("scale_unstable", "The weight is not steady yet. Wait a moment and try again.");
}
