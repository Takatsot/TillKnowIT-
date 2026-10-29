/** South African time is UTC+2 all year. Dates are written 2026-10-26, the way the server's DateOnly is. */
const SA_MS = 2 * 3_600_000;

export function saDate(at: Date = new Date()): string {
  return new Date(at.getTime() + SA_MS).toISOString().slice(0, 10);
}

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** "2026-10-26" and "14:30" as written in South Africa become the instant the server stores. */
export function saInstant(date: string, time: string): string {
  return new Date(`${date}T${time}:00+02:00`).toISOString();
}

export function saTime(value: string | Date): string {
  const d = typeof value === "string" ? new Date(value) : value;
  return Number.isNaN(d.getTime()) ? "" : new Date(d.getTime() + SA_MS).toISOString().slice(11, 16);
}

export function saDateTime(value: string | Date | null | undefined): string {
  if (!value) return "";
  const d = typeof value === "string" ? new Date(value) : value;
  return Number.isNaN(d.getTime()) ? "" : `${saDate(d)} ${saTime(d)}`;
}
