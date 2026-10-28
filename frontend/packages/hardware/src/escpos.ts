/**
 * ESC/POS is the command language almost every receipt printer speaks. This builds the bytes for a slip; sending them is the job of a transport (Web Serial, WebUSB,
 * Bluetooth, or a print service), which is chosen per device and sits behind IPrinter. Nothing here touches hardware, so it is all testable.
 */

const ESC = 0x1b;
const GS = 0x1d;
const LF = 0x0a;

export type Align = "left" | "center" | "right";

/** Paper widths in characters of the default font: 58 mm paper holds 32, 80 mm paper holds 48. */
export const PAPER_COLUMNS = { mm58: 32, mm80: 48 } as const;

/**
 * Receipt printers do not speak Unicode. Code page 858 (Western Europe with the euro) covers the letters South African names and shop words need;
 * anything it cannot show becomes "?" so a stray symbol never prints as a different character.
 */
const CP858: Record<string, number> = {
  "Ç": 0x80, "ü": 0x81, "é": 0x82, "â": 0x83, "ä": 0x84, "à": 0x85, "å": 0x86, "ç": 0x87, "ê": 0x88, "ë": 0x89, "è": 0x8a, "ï": 0x8b, "î": 0x8c, "ì": 0x8d,
  "Ä": 0x8e, "Å": 0x8f, "É": 0x90, "æ": 0x91, "Æ": 0x92, "ô": 0x93, "ö": 0x94, "ò": 0x95, "û": 0x96, "ù": 0x97, "ÿ": 0x98, "Ö": 0x99, "Ü": 0x9a, "ø": 0x9b,
  "£": 0x9c, "Ø": 0x9d, "á": 0xa0, "í": 0xa1, "ó": 0xa2, "ú": 0xa3, "ñ": 0xa4, "Ñ": 0xa5, "€": 0xd5, "ß": 0xe1,
};

export function encodeText(text: string): number[] {
  const bytes: number[] = [];
  for (const ch of text.normalize("NFC")) {
    const code = ch.codePointAt(0)!;
    if (code === 0x0a) bytes.push(LF);
    else if (code >= 0x20 && code < 0x7f) bytes.push(code);
    else bytes.push(CP858[ch] ?? 0x3f);
  }
  return bytes;
}

/** Builds one print job. Every method returns the builder so a slip reads top to bottom. */
export class EscPos {
  private readonly bytes: number[] = [];

  constructor() {
    this.bytes.push(ESC, 0x40);        // initialise: forget whatever the last job left behind
    this.bytes.push(ESC, 0x74, 19);    // code page 858
  }

  text(value: string): this {
    this.bytes.push(...encodeText(value));
    return this;
  }

  line(value = ""): this {
    return this.text(value).feed(1);
  }

  feed(lines: number): this {
    for (let i = 0; i < Math.min(Math.max(lines, 0), 20); i++) this.bytes.push(LF);
    return this;
  }

  align(align: Align): this {
    this.bytes.push(ESC, 0x61, align === "left" ? 0 : align === "center" ? 1 : 2);
    return this;
  }

  bold(on: boolean): this {
    this.bytes.push(ESC, 0x45, on ? 1 : 0);
    return this;
  }

  /** Double height and width for the total line, normal when off. */
  large(on: boolean): this {
    this.bytes.push(GS, 0x21, on ? 0x11 : 0x00);
    return this;
  }

  /** A QR code (model 2). It is how a slip carries its link when the customer wants it on a phone. Data up to 200 characters. */
  qr(data: string, moduleSize = 6): this {
    const payload = encodeText(data).slice(0, 200);
    const size = Math.min(Math.max(Math.round(moduleSize), 1), 16);
    const len = payload.length + 3;
    this.bytes.push(GS, 0x28, 0x6b, 4, 0, 0x31, 0x41, 0x32, 0);          // model 2
    this.bytes.push(GS, 0x28, 0x6b, 3, 0, 0x31, 0x43, size);              // module size
    this.bytes.push(GS, 0x28, 0x6b, 3, 0, 0x31, 0x45, 0x31);              // error correction M
    this.bytes.push(GS, 0x28, 0x6b, len & 0xff, len >> 8, 0x31, 0x50, 0x30, ...payload);   // store the data
    this.bytes.push(GS, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30);              // print it
    return this;
  }

  /** Partial cut after feeding the paper past the blade. */
  cut(): this {
    this.feed(3);
    this.bytes.push(GS, 0x56, 0x42, 0);
    return this;
  }

  /** Opens a cash drawer wired to the printer's drawer port (pin 2). On and off times are in units of 2 ms. */
  openDrawer(): this {
    this.bytes.push(ESC, 0x70, 0, 25, 250);
    return this;
  }

  build(): Uint8Array {
    return Uint8Array.from(this.bytes);
  }
}

/** The drawer pulse on its own, for opening the till without printing. */
export const drawerKick = (): Uint8Array => new EscPos().openDrawer().build();
