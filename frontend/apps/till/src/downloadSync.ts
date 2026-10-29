import type { Api } from "@tillknowit/api-client";
import { outboxCounts, retryDelaySeconds, type Outbox, type Result } from "@tillknowit/domain";
import { applyProductPull, productsAsOf, savePromotions, type ProductPull, type ServerPromotion } from "./catalogueStore";
import type { KeyValueStore } from "./localData";
import { chooseIncomingPack, clearPack, loadPack, mapPack, savePack, type ServerPack } from "./packStore";
import { clearDeviceConfig } from "./registration";
import { clearPinStates, loadStaff, mapStaff, saveStaff, type ServerStaff, type StaffRecord } from "./staffStore";
import type { DeviceConfig } from "./syncRunner";

/**
 * The download half of sync: what the server tells the till (who works here, what is sold, the promotions, the business pack) and whether the owner has asked for
 * the till to be wiped. Selling never waits for it: the till sells with what it already has, and this brings the copy up to date whenever there is a connection.
 */

export type DeviceStatus = "approved" | "pending" | "disabled" | "unknown";

export interface PullStatus {
  deviceStatus: DeviceStatus;
  /** When everything last came down without a problem (ISO), or null if it never has. */
  lastSyncAt: string | null;
  /** The last problem, in words for the cashier, or null. */
  lastError: string | null;
  /** "waiting": the owner asked for a wipe but sales are still waiting to be sent, so nothing was cleared yet. */
  wipe: "none" | "waiting" | "done";
  waitingSales: number;
}

export const initialStatus: PullStatus = { deviceStatus: "unknown", lastSyncAt: null, lastError: null, wipe: "none", waitingSales: 0 };

export interface PullResult {
  status: PullStatus;
  /** How long to wait before the next pull. */
  retry: "later" | "soon" | "backoff";
  changed: { staff: boolean; products: boolean; promotions: boolean; pack: boolean };
  /** The till was wiped and is no longer registered. */
  wiped: boolean;
  /** The parts the server refused (for example a feature the plan does not include): kept for support, not shown as an error. */
  notes: string[];
}

export interface PullDeps {
  api: Api;
  device: DeviceConfig;
  kv: KeyValueStore;
  outbox: Outbox;
  /** The person at the till, for the calls that name a staff member. Otherwise the first person downloaded. */
  staffId?: () => string | null;
  now?: () => Date;
  /** Sends the sales that are waiting. Used before a wipe, so nothing sold is lost. */
  flush?: () => Promise<void>;
}

const STATUS_KEY = "sync.status";

export async function loadStatus(kv: KeyValueStore): Promise<PullStatus> {
  return { ...initialStatus, ...((await kv.get<Partial<PullStatus>>(STATUS_KEY)) ?? {}) };
}

const isTransient = (code: string): boolean => code === "network.unreachable" || /^http\.5/.test(code) || code === "http.429" || code === "http.408";

const preference: ReadonlyArray<StaffRecord["role"]> = ["owner", "manager", "cashier"];

/** Who to name in the calls that need a staff member: the person at the till, else the most senior person downloaded. */
function pickStaffId(preferred: string | null | undefined, staff: readonly StaffRecord[]): string | null {
  if (preferred && staff.some((s) => s.id === preferred)) return preferred;
  return [...staff].sort((a, b) => preference.indexOf(a.role) - preference.indexOf(b.role))[0]?.id ?? preferred ?? null;
}

/** Clears everything downloaded and the till's own books. Sales still waiting must have been sent (or refused) first: see `pullAll`. */
async function wipeLocalData(deps: PullDeps): Promise<void> {
  await deps.kv.clear();
  for (const e of await deps.outbox.all()) await deps.outbox.remove(e.id);
  clearPack();
  clearPinStates();
}

export async function pullAll(deps: PullDeps): Promise<PullResult> {
  const { api, device, kv } = deps;
  const now = (deps.now ?? (() => new Date()))();
  const notes: string[] = [];
  const changed = { staff: false, products: false, promotions: false, pack: false };
  const previous = await loadStatus(kv);
  let status: PullStatus = { ...previous };
  const finish = async (retry: PullResult["retry"], wiped = false): Promise<PullResult> => {
    if (!wiped) await kv.set(STATUS_KEY, status);
    return { status, retry, changed, wiped, notes };
  };

  // ---- 1. who are we to the server, and has the owner asked for a wipe ----
  const checkIn: Result<{ status: string; wipeRequested: boolean }> = await api.post(`/devices/${device.deviceId}/check-in`);
  if (!checkIn.ok) {
    status = { ...status, lastError: checkIn.message };
    return finish(isTransient(checkIn.code) ? "backoff" : "later");
  }

  if (checkIn.value.wipeRequested) {
    await deps.flush?.();
    const counts = await outboxCounts(deps.outbox);
    if (counts.waiting > 0) {
      status = { ...status, wipe: "waiting", waitingSales: counts.waiting, lastError: null };
      return finish("soon");
    }
    await wipeLocalData(deps);
    const confirmed = await api.post(`/devices/${device.deviceId}/wipe-confirmed`);
    if (!confirmed.ok) {
      // The data is gone; the server still shows the request, so the next check-in confirms it.
      status = { ...initialStatus, wipe: "waiting", lastError: confirmed.message };
      await kv.set(STATUS_KEY, status);
      return { status, retry: "soon", changed, wiped: false, notes };
    }
    clearDeviceConfig();
    status = { ...initialStatus, wipe: "done" };
    return finish("later", true);
  }

  const reported = checkIn.value.status.toLowerCase();
  const deviceStatus: DeviceStatus = reported === "approved" || reported === "pending" || reported === "disabled" ? reported : "unknown";
  status = { ...status, deviceStatus, wipe: "none", waitingSales: 0, lastError: null };
  if (deviceStatus !== "approved") return finish(deviceStatus === "pending" ? "soon" : "later");

  let transientProblem: string | null = null;
  const part = <T>(result: Result<T>, label: string): T | null => {
    if (result.ok) return result.value;
    if (isTransient(result.code)) transientProblem ??= result.message;
    else notes.push(`${label}: ${result.code}`);
    return null;
  };

  // ---- 2. staff ----
  const staffRows = part(await api.get<ServerStaff[]>(`/shops/${device.shopId}/devices/${device.deviceId}/staff`), "staff");
  if (staffRows) {
    await saveStaff(kv, mapStaff(staffRows));
    changed.staff = true;
  }
  const staff = await loadStaff(kv);
  const staffId = pickStaffId(deps.staffId?.(), staff);

  // ---- 3. products (only what changed since last time) ----
  const since = await productsAsOf(kv);
  const pull = part(await api.get<ProductPull>(`/shops/${device.shopId}/sync/products`, { deviceId: device.deviceId, since }), "products");
  if (pull) {
    await applyProductPull(kv, pull);
    changed.products = true;
  }

  // ---- 4. promotions and the business pack both name a staff member ----
  if (staffId) {
    const promotions = await api.get<ServerPromotion[]>(`/shops/${device.shopId}/promotions`, { deviceId: device.deviceId, staffId });
    // A shop whose pack has promotions off is refused with feature.not_enabled: it has none to sell.
    if (!promotions.ok && promotions.code === "feature.not_enabled") {
      await savePromotions(kv, []);
      changed.promotions = true;
    } else {
      const rows = part(promotions, "promotions");
      if (rows) {
        await savePromotions(kv, rows);
        changed.promotions = true;
      }
    }

    const packRow = part(await api.get<ServerPack>(`/shops/${device.shopId}/devices/${device.deviceId}/business-pack`), "business-pack");
    if (packRow) {
      const current = loadPack(device.shopId);
      const chosen = chooseIncomingPack(current, mapPack(packRow));
      if (chosen !== current) {
        savePack(chosen);
        changed.pack = true;
      }
    }
  } else notes.push("promotions: no staff on this till yet");

  if (transientProblem) {
    status = { ...status, lastError: transientProblem };
    return finish("backoff");
  }
  status = { ...status, lastSyncAt: now.toISOString(), lastError: null };
  return finish("later");
}

// ---------------------------------------------------------------- the runner

export interface DownloadSyncOptions {
  run: () => Promise<PullResult>;
  onResult: (result: PullResult) => void;
  isOnline?: () => boolean;
  /** Seconds between pulls when all is well. */
  intervalSeconds?: number;
  /** Seconds between pulls while the device waits for the owner's approval, or a wipe waits for sales to be sent. */
  soonSeconds?: number;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

export interface DownloadSyncHandle {
  /** Pull now (the connection came back, or the person asked). Only one pull runs at a time. */
  nudge(): void;
  stop(): void;
}

/**
 * Pulls when the till starts, again every few minutes, and when nudged. A failed pull is retried with the same back-off as sending sales (5 seconds, doubling to
 * 5 minutes). Offline it does nothing and waits for the connection to come back.
 */
export function startDownloadSync(options: DownloadSyncOptions): DownloadSyncHandle {
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
    if (!isOnline()) return schedule(options.intervalSeconds ?? 300);
    running = true;
    try {
      const result = await options.run();
      failures = result.retry === "backoff" ? failures + 1 : 0;
      options.onResult(result);
      if (result.wiped) return;
      schedule(result.retry === "backoff" ? retryDelaySeconds(failures) : result.retry === "soon" ? (options.soonSeconds ?? 60) : (options.intervalSeconds ?? 300));
    } catch {
      failures++;
      schedule(retryDelaySeconds(failures));
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
