import { formatRand, parseRandToCents } from "@tillknowit/domain";

export { formatRand, parseRandToCents };

/** Dates as South Africans write them: 2026-10-26, and with the time in South African time. */
export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return "";
  const d = typeof value === "string" ? new Date(value.length === 10 ? `${value}T00:00:00Z` : value) : value;
  if (Number.isNaN(d.getTime())) return "";
  const local = value instanceof Date || (typeof value === "string" && value.length > 10) ? new Date(d.getTime() + 2 * 3_600_000) : d;
  return local.toISOString().slice(0, 10);
}

export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return "";
  const d = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return "";
  const local = new Date(d.getTime() + 2 * 3_600_000);
  return `${local.toISOString().slice(0, 10)} ${local.toISOString().slice(11, 16)}`;
}

export const percent = (n: number): string => `${Math.round(n * 10) / 10}%`;

/** Saves a downloaded report through the browser. */
export function saveFile(file: { blob: Blob; fileName: string }): void {
  const url = URL.createObjectURL(file.blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = file.fileName;
  a.click();
  URL.revokeObjectURL(url);
}
