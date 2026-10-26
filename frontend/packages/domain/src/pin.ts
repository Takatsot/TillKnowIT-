/**
 * Offline PIN check. Hashes use the same format as the server (Pbkdf2PinHasher in C#):
 *   pbkdf2-sha256$iterations$salt(base64)$hash(base64)
 * so the till can check a PIN against the synced hash with no network.
 */
export const MAX_PIN_ATTEMPTS = 5;
export const LOCK_DURATION_MS = 5 * 60 * 1000;
const PREFIX = "pbkdf2-sha256";
const ITERATIONS = 100_000;

export const isValidPin = (pin: string): boolean => /^\d{4,6}$/.test(pin);

const enc = new TextEncoder();

const toBase64 = (bytes: Uint8Array): string => btoa(String.fromCharCode(...bytes));
const fromBase64 = (text: string): Uint8Array => Uint8Array.from(atob(text), (c) => c.charCodeAt(0));

async function derive(pin: string, salt: Uint8Array, iterations: number, bytes: number): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey("raw", enc.encode(pin), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: salt as BufferSource, iterations },
    key,
    bytes * 8,
  );
  return new Uint8Array(bits);
}

/** Used by tests and the dev fixture. Real hashes are made by the server when staff are added. */
export async function hashPin(pin: string, salt: Uint8Array = crypto.getRandomValues(new Uint8Array(16))): Promise<string> {
  const hash = await derive(pin, salt, ITERATIONS, 32);
  return `${PREFIX}$${ITERATIONS}$${toBase64(salt)}$${toBase64(hash)}`;
}

export async function verifyPin(pin: string, stored: string): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 4 || parts[0] !== PREFIX) return false;
  const iterations = Number(parts[1]);
  if (!Number.isInteger(iterations) || iterations <= 0) return false;
  try {
    const expected = fromBase64(parts[3] as string);
    const actual = await derive(pin, fromBase64(parts[2] as string), iterations, expected.length);
    let diff = actual.length ^ expected.length;
    for (let i = 0; i < expected.length; i++) diff |= (actual[i] ?? 0) ^ (expected[i] ?? 0);
    return diff === 0;
  } catch {
    return false;
  }
}

export interface PinState {
  failedAttempts: number;
  /** Epoch milliseconds, or null when not locked. */
  lockedUntil: number | null;
}

export const freshPinState: PinState = { failedAttempts: 0, lockedUntil: null };

export const isLocked = (state: PinState, now: number): boolean => state.lockedUntil !== null && state.lockedUntil > now;

export type PinAttempt =
  | { status: "ok"; state: PinState }
  | { status: "wrong"; state: PinState; attemptsLeft: number }
  | { status: "locked"; state: PinState; lockedUntil: number };

/**
 * One try at unlocking: the same lockout the server applies. Five wrong tries lock for five minutes,
 * and a locked PIN is refused even when it is right.
 */
export async function attemptPin(pin: string, storedHash: string, state: PinState, now: number): Promise<PinAttempt> {
  if (isLocked(state, now)) return { status: "locked", state, lockedUntil: state.lockedUntil as number };

  if (await verifyPin(pin, storedHash)) return { status: "ok", state: freshPinState };

  const failedAttempts = state.failedAttempts + 1;
  if (failedAttempts >= MAX_PIN_ATTEMPTS) {
    const lockedUntil = now + LOCK_DURATION_MS;
    return { status: "locked", state: { failedAttempts: 0, lockedUntil }, lockedUntil };
  }
  return {
    status: "wrong",
    state: { failedAttempts, lockedUntil: null },
    attemptsLeft: MAX_PIN_ATTEMPTS - failedAttempts,
  };
}
