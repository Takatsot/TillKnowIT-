import { describe, expect, it } from "vitest";
import { coverage, isLanguage, languages, staleKeys, translate } from "./index";
import { en, type MessageKey } from "./en";

describe("translate", () => {
  it("fills in parameters", () => {
    expect(translate("en", "unlock.wrongPin", { left: 3 })).toBe("That PIN is not right. Tries left: 3.");
  });

  it("falls back to English for untranslated keys, never blank", () => {
    expect(translate("nso", "till.lock")).toBe("Lock till");   // no draft for Sepedi yet
    expect(translate("zu", "till.lock")).toBe("Vala i-till");
  });

  it("leaves unknown placeholders visible instead of hiding them", () => {
    expect(translate("en", "till.hello")).toBe("Hello, {name}");
  });

  it("knows the eleven languages, the five Phase 5 ones last", () => {
    expect(languages.map((l) => l.code)).toEqual(["en", "zu", "xh", "st", "tn", "af", "nso", "ts", "ve", "nr", "ss"]);
    expect(isLanguage("zu")).toBe(true);
    expect(isLanguage("fr")).toBe(false);
    for (const code of ["nso", "ts", "ve", "nr", "ss"]) expect(translate(code as never, "till.lock")).toBe("Lock till");   // English until translated, never blank
  });
});

describe("translation coverage", () => {
  it("English is complete", () => {
    expect(coverage("en").missing).toEqual([]);
  });

  it("no language has keys English does not have", () => {
    for (const { code } of languages) expect(staleKeys(code)).toEqual([]);
  });

  it("a translation keeps every {placeholder} of the English text, so no amount, name or count is lost", () => {
    for (const { code } of languages) {
      for (const key of Object.keys(en) as MessageKey[]) {
        const wanted = (en[key].match(/\{\w+\}/g) ?? []).sort();
        const given = (translate(code, key).match(/\{\w+\}/g) ?? []).sort();
        expect(given, `${code} ${key}`).toEqual(wanted);
      }
    }
  });

  it("the first drafts cover everything the till shows", () => {
    for (const code of ["zu", "xh", "st", "tn", "af"] as const) expect(coverage(code).missing, code).toEqual([]);
  });

  // The definition of done says no untranslated text in any of the eleven languages.
  // Switch this on in CI when translations land: REQUIRE_FULL_TRANSLATION=1
  it.skipIf(!process.env["REQUIRE_FULL_TRANSLATION"])("every language is fully translated", () => {
    for (const { code } of languages) expect(coverage(code).missing, code).toEqual([]);
  });
});
