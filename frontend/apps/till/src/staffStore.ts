import { freshPinState, hashPin, type PinState } from "@tillknowit/domain";
import type { KeyValueStore } from "./localData";
import { loadDeviceConfig } from "./syncRunner";

export interface StaffRecord {
  /** The shop membership id from the server. Sales use it as their staffId. */
  id: string;
  name: string;
  role: "owner" | "manager" | "cashier";
  /** PBKDF2 hash from the server. The PIN itself is never stored. */
  pinHash: string;
}

/** A staff member as the server sends them (DeviceStaffResponse). */
export interface ServerStaff {
  staffId: string;
  name: string;
  role: string;
  pinHash: string | null;
  disabled: boolean;
}

const STAFF_KEY = "staff";
const roles: ReadonlyArray<StaffRecord["role"]> = ["owner", "manager", "cashier"];

/** Who can sign in: someone who is switched off, has no PIN set yet, or has a role the till does not know is left out. */
export function mapStaff(rows: readonly ServerStaff[]): StaffRecord[] {
  return rows
    .filter((r) => !r.disabled && typeof r.pinHash === "string" && r.pinHash !== "" && (roles as readonly string[]).includes(r.role.toLowerCase()))
    .map((r) => ({ id: r.staffId, name: r.name, role: r.role.toLowerCase() as StaffRecord["role"], pinHash: r.pinHash as string }));
}

export async function saveStaff(kv: KeyValueStore, staff: readonly StaffRecord[]): Promise<void> {
  await kv.set(STAFF_KEY, staff);
}

/**
 * Staff for this till: the people downloaded from the server. A registered till never shows example people. Only a development build on a till that is not
 * registered gets two examples, so no default PIN can ever ship.
 */
export async function loadStaff(kv?: KeyValueStore): Promise<StaffRecord[]> {
  const saved = kv ? await kv.get<StaffRecord[]>(STAFF_KEY) : undefined;
  if (saved) return saved;
  if (loadDeviceConfig() !== null || !import.meta.env.DEV) return [];
  return [
    { id: "dev-thandi", name: "Thandi", role: "cashier", pinHash: await hashPin("1234") },
    { id: "dev-sipho", name: "Sipho", role: "manager", pinHash: await hashPin("4321") },
  ];
}

const key = (staffId: string) => `tillknowit.pin.${staffId}`;

/** Failed tries and the lock must survive a restart, or closing the app would be a way round the lock. */
export function loadPinState(staffId: string): PinState {
  try {
    const raw = localStorage.getItem(key(staffId));
    if (!raw) return freshPinState;
    const parsed = JSON.parse(raw) as Partial<PinState>;
    return {
      failedAttempts: Number.isInteger(parsed.failedAttempts) ? (parsed.failedAttempts as number) : 0,
      lockedUntil: typeof parsed.lockedUntil === "number" ? parsed.lockedUntil : null,
    };
  } catch {
    return freshPinState;
  }
}

export function savePinState(staffId: string, state: PinState): void {
  try {
    localStorage.setItem(key(staffId), JSON.stringify(state));
  } catch {
    // Storage can be unavailable (private window, full disk). The lock still holds for this session.
  }
}

/** A wipe forgets every PIN lock too. */
export function clearPinStates(): void {
  try {
    for (const k of Object.keys(localStorage)) if (k.startsWith("tillknowit.pin.")) localStorage.removeItem(k);
  } catch {
    // Nothing to clear.
  }
}
