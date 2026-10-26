import { freshPinState, hashPin, type PinState } from "@tillknowit/domain";

export interface StaffRecord {
  id: string;
  name: string;
  role: "owner" | "manager" | "cashier";
  /** PBKDF2 hash from the server. The PIN itself is never stored. */
  pinHash: string;
}

/**
 * Staff for this till. Later this reads the synced memberships table (local SQLite via PowerSync).
 * Until sync exists, development builds get two example people and production builds get nobody,
 * so no default PIN can ever ship.
 */
export async function loadStaff(): Promise<StaffRecord[]> {
  if (!import.meta.env.DEV) return [];
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
