/** Money is whole cents, never decimals. */
export type Cents = number;

const rand = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "ZAR" });

export function formatRand(cents: Cents): string {
  return rand.format(cents / 100);
}

/** "18", "18.5" and "18,50" all mean R18.50. Returns null for anything else. */
export function parseRandToCents(text: string): Cents | null {
  const cleaned = text.trim().replace(/^R\s*/i, "").replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  const [whole = "0", fraction = ""] = cleaned.split(".");
  return Number(whole) * 100 + Number(fraction.padEnd(2, "0"));
}

/** The VAT inside a VAT-inclusive total. 11500 at 15% contains 1500. */
export function vatIncluded(totalCents: Cents, ratePercent: number): Cents {
  return Math.round((totalCents * ratePercent) / (100 + ratePercent));
}

/** Suggested selling price from cost and markup, rounded up to the next 10 cents so prices stay tidy. */
export function suggestedPrice(costCents: Cents, markupPercent: number): Cents {
  const raw = costCents * (1 + markupPercent / 100);
  return Math.ceil(raw / 10) * 10;
}

export function profitCents(priceCents: Cents, costCents: Cents, quantity: number): Cents {
  return (priceCents - costCents) * quantity;
}
