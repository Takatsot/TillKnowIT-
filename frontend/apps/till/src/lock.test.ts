import { describe, expect, it } from "vitest";
import { minutesLeft } from "./lock";

describe("minutesLeft", () => {
  const now = 1_000_000;
  it("rounds up so the screen never says 0 while locked", () => {
    expect(minutesLeft(now + 5 * 60_000, now)).toBe(5);
    expect(minutesLeft(now + 4 * 60_000 + 1, now)).toBe(5);
    expect(minutesLeft(now + 1, now)).toBe(1);
    expect(minutesLeft(now - 10, now)).toBe(1);
  });
});
