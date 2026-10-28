import { describe, expect, it } from "vitest";
import { isUuid, newId } from "./uuid";

describe("newId", () => {
  it("makes a version 7 id with the variant bits and the time in front", () => {
    const id = newId(Date.UTC(2026, 9, 26, 8, 0, 0), (b) => b.fill(0xab));

    expect(isUuid(id)).toBe(true);
    expect(id[14]).toBe("7");
    expect("89ab").toContain(id[19]!);
    expect(parseInt(id.replaceAll("-", "").slice(0, 12), 16)).toBe(Date.UTC(2026, 9, 26, 8, 0, 0));
  });

  it("sorts by the time it was made and two made together differ", () => {
    const earlier = newId(1_000_000_000_000);
    const later = newId(1_000_000_000_001);

    expect(earlier < later).toBe(true);
    expect(newId(5)).not.toBe(newId(5));
  });

  it("is not fooled by times beyond 32 bits", () => {
    expect(newId(2 ** 40 + 5, (b) => b.fill(0)).slice(0, 13)).toBe("010000000005".replace(/^(.{8})(.{4})$/, "$1-$2"));
  });

  it("recognises what is and is not an id", () => {
    expect(isUuid("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b")).toBe(true);
    expect(isUuid("dev-thandi")).toBe(false);
  });
});
