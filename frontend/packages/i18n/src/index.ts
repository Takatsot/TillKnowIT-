import { en, type MessageKey } from "./en";

export type { MessageKey };

export const languages = [
  { code: "en", name: "English" },
  { code: "zu", name: "isiZulu" },
  { code: "xh", name: "isiXhosa" },
  { code: "st", name: "Sesotho" },
  { code: "tn", name: "Setswana" },
  { code: "af", name: "Afrikaans" },
] as const;

export type LanguageCode = (typeof languages)[number]["code"];

type Catalogue = Partial<Record<MessageKey, string>>;

// Translations are added by translators, one file per language. Until a key is translated the
// app shows the English text, so nothing is ever blank. `coverage` reports what is still missing.
const catalogues: Record<LanguageCode, Catalogue> = { en, zu: {}, xh: {}, st: {}, tn: {}, af: {} };

export const isLanguage = (code: string): code is LanguageCode => code in catalogues;

export function translate(language: LanguageCode, key: MessageKey, params: Record<string, string | number> = {}): string {
  const text = catalogues[language][key] ?? en[key];
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}

export interface Coverage {
  language: LanguageCode;
  translated: number;
  total: number;
  missing: MessageKey[];
}

/** For the translation report and the release gate: which keys still show English in this language. */
export function coverage(language: LanguageCode): Coverage {
  const keys = Object.keys(en) as MessageKey[];
  const missing = keys.filter((k) => catalogues[language][k] === undefined);
  return { language, translated: keys.length - missing.length, total: keys.length, missing };
}

/** Keys a translation has that English no longer has (renamed or removed). */
export function staleKeys(language: LanguageCode): string[] {
  return Object.keys(catalogues[language]).filter((k) => !(k in en));
}
