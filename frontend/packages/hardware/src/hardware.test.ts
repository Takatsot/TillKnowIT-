import { describe, expect, it } from "vitest";
import {
  EscPos, FakeCashDrawer, FakeKitchenDisplay, FakePrinter, FakeScale, HardwareError, buildKitchenTicket, buildSlip, drawerKick, encodeText, rand, readStableWeight, saDateTime,
} from "./index";

/** Reads back only the printable text of a job, skipping the command bytes, the way the printer does. */
const text = (bytes: Uint8Array): string => {
  let out = "";
  for (let i = 0; i < bytes.length; ) {
    const b = bytes[i]!;
    if (b === 0x1b) i += bytes[i + 1] === 0x40 ? 2 : bytes[i + 1] === 0x70 ? 5 : 3;
    else if (b === 0x1d && bytes[i + 1] === 0x28) i += 5 + (bytes[i + 3]! + bytes[i + 4]! * 256) - 0;
    else if (b === 0x1d && bytes[i + 1] === 0x56) i += 4;
    else if (b === 0x1d) i += 3;
    else {
      if (b >= 0x20 && b < 0x7f) out += String.fromCharCode(b);
      else if (b === 0x0a) out += "\n";
      i++;
    }
  }
  return out;
};
const contains = (bytes: Uint8Array, seq: number[]): boolean => bytes.some((_, i) => seq.every((b, k) => bytes[i + k] === b));

describe("ESC/POS", () => {
  it("starts every job by resetting the printer and choosing the code page", () => {
    expect([...new EscPos().build()]).toEqual([0x1b, 0x40, 0x1b, 0x74, 19]);
  });

  it("encodes plain text as it is and accents in code page 858, with a question mark for what it cannot show", () => {
    expect(encodeText("Café Ñandi €5")).toEqual([0x43, 0x61, 0x66, 0x82, 0x20, 0xa5, 0x61, 0x6e, 0x64, 0x69, 0x20, 0xd5, 0x35]);
    expect(encodeText("日本")).toEqual([0x3f, 0x3f]);
    expect(encodeText("a\nb")).toEqual([0x61, 0x0a, 0x62]);
  });

  it("builds alignment, bold, large type, cut and the drawer pulse", () => {
    const job = new EscPos().align("center").bold(true).large(true).cut().openDrawer().build();

    expect(contains(job, [0x1b, 0x61, 1])).toBe(true);
    expect(contains(job, [0x1b, 0x45, 1])).toBe(true);
    expect(contains(job, [0x1d, 0x21, 0x11])).toBe(true);
    expect(contains(job, [0x1d, 0x56, 0x42, 0])).toBe(true);
    expect([...drawerKick().slice(-5)]).toEqual([0x1b, 0x70, 0, 25, 250]);
  });

  it("builds a QR code with the right length bytes and never more than 200 characters", () => {
    const job = new EscPos().qr("https://slip.example/s/abc").build();
    const store = [0x1d, 0x28, 0x6b, 29, 0, 0x31, 0x50, 0x30];   // 26 characters + 3
    expect(contains(job, store)).toBe(true);
    expect(contains(job, [0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30])).toBe(true);

    const long = new EscPos().qr("x".repeat(500)).build();
    expect(contains(long, [0x1d, 0x28, 0x6b, 203 & 0xff, 203 >> 8, 0x31, 0x50, 0x30])).toBe(true);
  });

  it("limits how far one feed can run the paper", () => {
    expect(new EscPos().feed(1000).build().length).toBe(5 + 20);
  });
});

describe("slips", () => {
  const sale = {
    shopName: "Mama's Spaza",
    lines: [{ name: "Bread", quantity: 2, totalCents: 3_600 }, { name: "Milk 1L", quantity: 1, totalCents: 2_199 }],
    totalCents: 5_799, vatCents: 756, paymentMethod: "Cash", soldAt: new Date("2026-10-26T08:04:00Z"),
  };

  it("shows the date as South Africans read it", () => {
    expect(saDateTime(new Date("2026-10-26T22:30:00Z"))).toBe("2026-10-27 00:30");
  });

  it("prints the lines, the total, the VAT inside it and how it was paid on a 32 column roll", () => {
    const out = text(buildSlip(sale, 32));

    expect(out).toContain("Mama's Spaza");
    expect(out).toContain("2026-10-26 10:04");
    expect(out).toMatch(/^2 x Bread +R36\.00$/m);
    expect(out).toMatch(/^Milk 1L +R21\.99$/m);
    expect(out).toMatch(/^TOTAL +R57\.99$/m);
    expect(out).toMatch(/^VAT included +R7\.56$/m);
    expect(out).toMatch(/^Paid by +Cash$/m);
    expect(out.split("\n").find((l) => l.startsWith("TOTAL"))).toHaveLength(32);   // the price sits at the right edge
    for (const l of out.split("\n")) expect(l.length).toBeLessThanOrEqual(32);
  });

  it("prints a QR code only when there is a link, opens the drawer only when asked and always cuts", () => {
    const without = buildSlip(sale);
    const withLink = buildSlip({ ...sale, slipUrl: "https://slip.example/s/abc" }, 48, { openDrawer: true });

    expect(contains(without, [0x1d, 0x28, 0x6b])).toBe(false);
    expect(contains(withLink, [0x1d, 0x28, 0x6b])).toBe(true);
    expect(contains(without, [0x1b, 0x70])).toBe(false);
    expect(contains(withLink, [0x1b, 0x70])).toBe(true);
    expect(contains(without, [0x1d, 0x56, 0x42, 0])).toBe(true);
  });

  it("keeps a long product name from pushing the price off the roll", () => {
    const out = text(buildSlip({ ...sale, lines: [{ name: "Extra long name of a product that will not fit", quantity: 1, totalCents: 9_999 }] }, 32));

    const line = out.split("\n").find((l) => l.endsWith("R99.99"))!;
    expect(line.length).toBeLessThanOrEqual(32);
  });

  it("writes money as plain ASCII", () => {
    expect(rand(5)).toBe("R0.05");
    expect(rand(123_456)).toBe("R1234.56");
  });

  it("prints a kitchen ticket with the table large, add-ons indented and notes marked", () => {
    const out = text(buildKitchenTicket({ id: "t1", table: "7", sentAt: new Date("2026-10-26T08:04:00Z"), lines: [{ quantity: 2, name: "Burger", modifiers: ["No onion", "Cheese"], note: "well done" }] }));

    expect(out).toContain("TABLE 7");
    expect(out).toContain("2 x Burger");
    expect(out).toContain("   + No onion");
    expect(out).toContain("   NOTE: well done");
    expect(text(buildKitchenTicket({ id: "t2", sentAt: new Date(), lines: [] }))).toContain("TAKEAWAY");
  });
});

describe("the scale", () => {
  const noWait = async () => {};

  it("sells a weight only once two steady readings agree", async () => {
    const scale = new FakeScale([{ grams: 0, stable: true }, { grams: 480, stable: false }, { grams: 502, stable: true }, { grams: 503, stable: true }]);

    expect(await readStableWeight(scale, { delay: noWait })).toBe(503);
  });

  it("starts again if the weight jumps, and never sells a zero", async () => {
    const jumpy = new FakeScale([{ grams: 500, stable: true }, { grams: 900, stable: true }, { grams: 900, stable: true }]);
    expect(await readStableWeight(jumpy, { delay: noWait })).toBe(900);

    const empty = new FakeScale([{ grams: 0, stable: true }]);
    await expect(readStableWeight(empty, { delay: noWait, attempts: 5 })).rejects.toMatchObject({ code: "scale_unstable" });
  });

  it("says so when the scale cannot be reached", async () => {
    const scale = new FakeScale([{ grams: 1, stable: true }]);
    scale.offline = true;

    await expect(readStableWeight(scale, { delay: noWait })).rejects.toMatchObject({ code: "scale_offline" });
    await expect(readStableWeight(scale, { delay: noWait })).rejects.toBeInstanceOf(HardwareError);
  });
});

describe("the fakes", () => {
  it("print, open and send when everything works and fail in words the cashier can act on when it does not", async () => {
    const printer = new FakePrinter();
    await printer.print(new Uint8Array([1]));
    expect(printer.jobs).toHaveLength(1);

    for (const [state, code] of [["offline", "printer_offline"], ["out_of_paper", "out_of_paper"], ["cover_open", "cover_open"]] as const) {
      printer.state = state;
      await expect(printer.print(new Uint8Array([2]))).rejects.toMatchObject({ code });
      expect(await printer.status()).toBe(state);
    }
    expect(printer.jobs).toHaveLength(1);

    const drawer = new FakeCashDrawer();
    await drawer.open();
    drawer.failing = true;
    await expect(drawer.open()).rejects.toMatchObject({ code: "drawer_failed" });
    expect(drawer.opened).toBe(1);

    const kitchen = new FakeKitchenDisplay();
    await kitchen.send({ id: "1", lines: [], sentAt: new Date() });
    kitchen.reachable = false;
    await expect(kitchen.send({ id: "2", lines: [], sentAt: new Date() })).rejects.toMatchObject({ code: "kitchen_unreachable" });
    expect(kitchen.orders).toHaveLength(1);
  });
});
