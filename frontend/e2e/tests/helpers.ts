import { expect, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";

export const API = process.env["API_URL"] ?? "http://localhost:5080";
const CACHE = path.join(import.meta.dirname, "..", ".session-cache.json");

/** The people the API's dev seed makes (backend/src/Host/TillKnowIT.Api/DevSeed.cs). */
export const PEOPLE = {
  owner: "0825550001",
  admin: "0825550005",
  supplier: "0825550006",
  parent: "0825550007",
} as const;

export interface Answer { accessToken: string; expiresAt: string; personId: string; displayName: string; language: string }

async function post<T>(p: string, body: unknown, token?: string): Promise<{ status: number; json: T }> {
  const r = await fetch(`${API}/api/v1${p}`, { method: "POST", headers: { "content-type": "application/json", ...(token ? { authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body) });
  return { status: r.status, json: (await r.json().catch(() => null)) as T };
}

/** Signs a seeded person in through the API (the dev server returns the code), and keeps the session for the hours it lasts: codes are limited to five an hour. */
export async function sessionFor(phone: string): Promise<Answer> {
  const cache: Record<string, Answer> = fs.existsSync(CACHE) ? JSON.parse(fs.readFileSync(CACHE, "utf8")) : {};
  const saved = cache[phone];
  if (saved && Date.parse(saved.expiresAt) > Date.now() + 5 * 60_000) return saved;
  const asked = await post<{ devCode?: string }>("/auth/otp/request", { phone });
  if (!asked.json?.devCode) throw new Error(`No dev code for ${phone} (status ${asked.status}). Is the API running in Development with the seed, and is the code limit used up?`);
  const verified = await post<Answer>("/auth/otp/verify", { phone, code: asked.json.devCode });
  if (verified.status !== 200) throw new Error(`Sign-in failed for ${phone}: ${verified.status}`);
  cache[phone] = verified.json;
  fs.writeFileSync(CACHE, JSON.stringify(cache));
  return verified.json;
}

/** What the apps keep in the browser once signed in, so a test can start already signed in. */
export async function startSignedIn(page: Page, phone: string): Promise<Answer> {
  const s = await sessionFor(phone);
  await page.addInitScript((value) => localStorage.setItem("tillknowit.session", JSON.stringify(value)), {
    personId: s.personId, shopId: "", supplierOrganisationId: "", deviceId: "", language: "en", token: s.accessToken, expiresAt: s.expiresAt, displayName: s.displayName,
  });
  return s;
}

export interface Problems { list: string[] }

/** Collects what should never happen: an uncaught error or a console error that is not just the server refusing a request. */
export function watchProblems(page: Page): Problems {
  const problems: Problems = { list: [] };
  page.on("pageerror", (e) => problems.list.push(`page error: ${e.message}`));
  page.on("console", (m) => {
    if (m.type() !== "error") return;
    const text = m.text();
    // A refused request is logged by the browser itself and is how the apps learn of "not allowed" and "not found": the screens handle those.
    if (/Failed to load resource/.test(text)) return;
    problems.list.push(`console error: ${text}`);
  });
  return problems;
}

/** The page must not be wider than the window: the sideways overflow a phone showed before. */
export async function expectNoSidewaysScroll(page: Page, where: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(over, `${where} is ${over}px wider than the window`).toBeLessThanOrEqual(1);
}

/** The row of buttons at the top of a screen that switches between its parts (Products, Import, Reps...). Found by shape: two or more buttons, nothing else, in one row. */
export async function tabBar(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const aria = [...document.querySelectorAll("main [role=tablist]")].find((l) => l.querySelectorAll("[role=tab]").length >= 2);
    if (aria) return [...aria.querySelectorAll("[role=tab]")].map((k) => (k.textContent ?? "").trim());
    const bars = [...document.querySelectorAll("main div, main nav")].filter((d) => {
      const kids = [...d.children];
      return kids.length >= 2 && kids.length <= 9 && kids.every((k) => k.tagName === "BUTTON") && !d.closest("form");
    });
    const first = bars[0];
    return first ? [...first.children].map((k) => (k.textContent ?? "").trim()) : [];
  });
}

/** Clicks each tab of the screen in turn, checking each one drew something and did not overflow. */
export async function walkTabs(page: Page, where: string, problems: Problems, phoneWidth = false): Promise<number> {
  const names = await tabBar(page);
  let clicked = 0;
  for (const name of names) {
    const tab = page.locator("main").getByRole("button", { name, exact: true }).or(page.locator("main").getByRole("tab", { name, exact: true })).first();
    if (!(await tab.isVisible()) || (await tab.isDisabled())) continue;
    await tab.click();
    clicked++;
    await page.waitForTimeout(500);
    const text = (await page.locator("main").innerText()).trim();
    expect(text.length, `${where} > ${name} is blank`).toBeGreaterThan(20);
    if (phoneWidth) await expectNoSidewaysScroll(page, `${where} > ${name}`);
  }
  expect(problems.list, `${where}`).toEqual([]);
  return clicked;
}
