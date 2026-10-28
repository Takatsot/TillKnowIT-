/**
 * Ids are made on the till, so a sale rung up offline already has the id the server will keep it under. UUID version 7: the time in front (so ids sort by when they
 * were made) and random bits behind (so two tills never make the same one).
 */
export function newId(now: number = Date.now(), random: (bytes: Uint8Array<ArrayBuffer>) => Uint8Array<ArrayBuffer> = (b) => crypto.getRandomValues(b)): string {
  const bytes = random(new Uint8Array(16));
  const ms = Math.max(0, Math.floor(now));
  // 48 bits of milliseconds, big endian. Divide rather than shift: JavaScript shifts are 32 bit.
  bytes[0] = Math.floor(ms / 2 ** 40) & 0xff;
  bytes[1] = Math.floor(ms / 2 ** 32) & 0xff;
  bytes[2] = Math.floor(ms / 2 ** 24) & 0xff;
  bytes[3] = Math.floor(ms / 2 ** 16) & 0xff;
  bytes[4] = Math.floor(ms / 2 ** 8) & 0xff;
  bytes[5] = ms & 0xff;
  bytes[6] = (bytes[6]! & 0x0f) | 0x70;   // version 7
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;   // variant 10
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export const isUuid = (value: string): boolean => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
