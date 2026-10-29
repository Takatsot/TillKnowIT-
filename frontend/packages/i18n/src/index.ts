import { en, type MessageKey } from "./en";

export type { MessageKey };

export const languages = [
  { code: "en", name: "English" },
  { code: "zu", name: "isiZulu" },
  { code: "xh", name: "isiXhosa" },
  { code: "st", name: "Sesotho" },
  { code: "tn", name: "Setswana" },
  { code: "af", name: "Afrikaans" },
  // Phase 5 (G6-2). Names are in the language itself. No translations have been supplied yet: each shows English until first-language speakers add and review them.
  { code: "nso", name: "Sepedi" },
  { code: "ts", name: "Xitsonga" },
  { code: "ve", name: "Tshivenda" },
  { code: "nr", name: "isiNdebele" },
  { code: "ss", name: "siSwati" },
] as const;

export type LanguageCode = (typeof languages)[number]["code"];

type Catalogue = Partial<Record<MessageKey, string>>;

// Translations are added by translators, one file per language. Until a key is translated the
// app shows the English text, so nothing is ever blank. `coverage` reports what is still missing.
const catalogues: Record<LanguageCode, Catalogue> = { en, zu: {}, xh: {}, st: {}, tn: {}, af: {}, nso: {}, ts: {}, ve: {}, nr: {}, ss: {} };

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

/**
 * Messages that belong to one screen area (billing, branches...) live next to that area, not in the shared English file. Each area defines its English text
 * once and any language can be added as data later; a missing text falls back to English, so nothing is ever blank.
 */
export type AreaMessages<K extends string> = { en: Readonly<Record<K, string>>; translations?: Partial<Record<LanguageCode, Partial<Record<K, string>>>> };

export function translateArea<K extends string>(area: AreaMessages<K>, language: LanguageCode, key: K, params: Record<string, string | number> = {}): string {
  const text = area.translations?.[language]?.[key] ?? area.en[key];
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}

/** Which texts of an area still show English in this language. */
export function areaCoverage<K extends string>(area: AreaMessages<K>, language: LanguageCode): K[] {
  return (Object.keys(area.en) as K[]).filter((k) => language !== "en" && area.translations?.[language]?.[k] === undefined);
}
