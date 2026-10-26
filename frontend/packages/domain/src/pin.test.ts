import { describe, expect, it } from "vitest";
import {
  attemptPin, freshPinState, hashPin, isLocked, isValidPin, LOCK_DURATION_MS, verifyPin, type PinState,
} from "./pin";

// Made with Node's crypto, and checked by the C# Pbkdf2PinHasher tests too, so till and server agree on the format.
const SHARED_VECTOR = "pbkdf2-sha256$100000$AAECAwQFBgcICQoLDA0ODw==$wmpH7CpIGbsLmht6VKVaCLZoFloVcHYAdmA4DgEdAUg=";

describe("pin hashing", () => {
  it("verifies the hash format shared with the server", async () => {
    expect(await verifyPin("4821", SHARED_VECTOR)).toBe(true);
    expect(await verifyPin("4822", SHARED_VECTOR)).toBe(false);
  });

  it("round-trips and is salted", async () => {
    const a = await hashPin("1234");
    const b = await hashPin("1234");
    expect(a).not.toBe(b);
    expect(a.startsWith("pbkdf2-sha256$100000$")).toBe(true);
    expect(await verifyPin("1234", a)).toBe(true);
    expect(await verifyPin("1235", a)).toBe(false);
  });

  it("rejects malformed hashes instead of throwing", async () => {
    expect(await verifyPin("1234", "garbage")).toBe(false);
    expect(await verifyPin("1234", "pbkdf2-sha256$x$AA==$AA==")).toBe(false);
    expect(await verifyPin("1234", "pbkdf2-sha256$100000$!!$!!")).toBe(false);
  });

  it("accepts only 4 to 6 digits", () => {
    for (const ok of ["1234", "123456"]) expect(isValidPin(ok)).toBe(true);
    for (const bad of ["123", "1234567", "12a4", ""]) expect(isValidPin(bad)).toBe(false);
  });
});

describe("lockout (same rules as the server)", () => {
  const NOW = Date.UTC(2026, 9, 26, 8, 0, 0);
  let hash: string;
  const wrong = async (state: PinState, now = NOW) => attemptPin("0000", hash, state, now);

  it("locks after five wrong tries and refuses even the right PIN", async () => {
    hash = await hashPin("1234");
    let state = freshPinState;
    for (let i = 1; i <= 4; i++) {
      const r = await wrong(state);
      expect(r.status).toBe("wrong");
      if (r.status === "wrong") expect(r.attemptsLeft).toBe(5 - i);
      state = r.state;
    }
    const fifth = await wrong(state);
    expect(fifth.status).toBe("locked");
    state = fifth.state;
    expect(isLocked(state, NOW)).toBe(true);

    expect((await attemptPin("1234", hash, state, NOW + 1000)).status).toBe("locked");
  });

  it("unlocks after five minutes", async () => {
    hash = await hashPin("1234");
    let state = freshPinState;
    for (let i = 0; i < 5; i++) state = (await wrong(state)).state;

    const later = NOW + LOCK_DURATION_MS + 1000;
    expect(isLocked(state, later)).toBe(false);
    expect((await attemptPin("1234", hash, state, later)).status).toBe("ok");
  });

  it("a correct PIN resets the count", async () => {
    hash = await hashPin("1234");
    let state = freshPinState;
    for (let i = 0; i < 4; i++) state = (await wrong(state)).state;
    state = (await attemptPin("1234", hash, state, NOW)).state;
    expect(state).toEqual(freshPinState);
    for (let i = 0; i < 4; i++) {
      const r = await wrong(state);
      expect(r.status).toBe("wrong");
      state = r.state;
    }
  });
});
