import { flushOutbox, outboxCounts, retryDelaySeconds, type Outbox, type SendBatch } from "@tillknowit/domain";
import { deviceHeaders } from "./deviceToken";

/** Which shop and till this device is. Set when the till is registered and approved; until then sales are kept but nothing is sent. */
export interface DeviceConfig {
  shopId: string;
  deviceId: string;
}

const CONFIG_KEY = "tillknowit.device";

export function loadDeviceConfig(): DeviceConfig | null {
  try {
    const raw = localStorage.getItem(CONFIG_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<DeviceConfig>;
    return typeof parsed.shopId === "string" && typeof parsed.deviceId === "string" ? { shopId: parsed.shopId, deviceId: parsed.deviceId } : null;
  } catch {
    return null;
  }
}

/** The upload over HTTP. Any answer that is not a success is an error, so the queue keeps what it has and tries again later. */
export function httpSend(baseUrl = "/api/v1", fetcher: typeof fetch = fetch, headers: () => Record<string, string> = deviceHeaders): SendBatch {
  return async (request) => {
    const response = await fetcher(`${baseUrl}/sync/upload`, { method: "POST", headers: { "Content-Type": "application/json", ...headers() }, body: JSON.stringify(request) });
    if (!response.ok) throw new Error(`The server answered ${response.status}.`);
    return (await response.json()) as { results: never[] };
  };
}

export interface SyncHandle {
  /** Try to send now (after a sale, or when the connection comes back). Safe to call often: only one send runs at a time. */
  nudge(): void;
  stop(): void;
}

export interface SyncOptions {
  outbox: Outbox;
  config: () => DeviceConfig | null;
  send: SendBatch;
  /** Told after every round how many sales are waiting and how many were refused. */
  onCounts: (counts: { waiting: number; refused: number }) => void;
  isOnline?: () => boolean;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

/**
 * Sends the queue whenever there is something to send and a connection to send it on, and backs off when it fails (5 seconds, doubling to 5 minutes).
 * Nothing here can lose a sale: it only ever removes one after the server has said it has it.
 */
export function startSync(options: SyncOptions): SyncHandle {
  const isOnline = options.isOnline ?? (() => navigator.onLine);
  const setTimer = options.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
  const clearTimer = options.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
  let timer: unknown = null;
  let running = false;
  let stopped = false;
  let failures = 0;

  const schedule = (seconds: number) => {
    if (timer !== null) clearTimer(timer);
    timer = stopped ? null : setTimer(() => void round(), seconds * 1000);
  };

  async function round(): Promise<void> {
    if (running || stopped) return;
    running = true;
    try {
      const config = options.config();
      const counts = await outboxCounts(options.outbox);
      if (config && counts.waiting > 0 && isOnline()) {
        const outcome = await flushOutbox(options.outbox, config, options.send);
        failures = outcome.failed ? failures + 1 : 0;
        if (outcome.failed) schedule(retryDelaySeconds(failures));
      }
      options.onCounts(await outboxCounts(options.outbox));
    } finally {
      running = false;
    }
  }

  void round();
  return {
    nudge: () => void round(),
    stop: () => {
      stopped = true;
      if (timer !== null) clearTimer(timer);
      timer = null;
    },
  };
}
