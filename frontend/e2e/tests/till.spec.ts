import { expect, test, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { API, expectNoSidewaysScroll, PEOPLE, sessionFor, tabBar, watchProblems } from "./helpers";

const SEED = path.join(import.meta.dirname, "..", "..", "..", "backend", "src", "Host", "TillKnowIT.Api", "dev-seed.md");

/** The shop and till the dev seed made, and a fresh one-time setup code: the owner resets the till's credential through the API (the code in the file is used up after the first run). */
async function seededTill() {
  const md = fs.readFileSync(SEED, "utf8");
  const shop = md.match(/Shop: .*?id `([^`]+)`/)?.[1];
  const till = md.match(/Till: .*?id `([^`]+)`/)?.[1];
  if (!shop || !till) throw new Error("dev-seed.md has no shop and till. Start the API in Development against an empty database.");
  const owner = await sessionFor(PEOPLE.owner);
  const headers = { authorization: `Bearer ${owner.accessToken}` };
  const reset = await fetch(`${API}/api/v1/devices/${till}/reset-credential?callerPersonId=${owner.personId}`, { method: "POST", headers });
  if (!reset.ok) throw new Error(`Could not reset the till's credential: ${reset.status}`);
  const code = ((await reset.json()) as { enrolmentCode: string }).enrolmentCode;
  const approved = await fetch(`${API}/api/v1/devices/${till}/approve?callerPersonId=${owner.personId}`, { method: "POST", headers });
  if (!approved.ok) throw new Error(`Could not approve the till: ${approved.status}`);
  return { shop, till, code };
}

async function registerAndUnlock(page: Page, staff: RegExp, pin: string) {
  const { shop, till, code } = await seededTill();
  await page.goto("http://localhost:5173");
  await page.getByLabel("Shop number").fill(shop);
  await page.getByLabel("Till number").fill(till);
  await page.getByLabel("Setup code").fill(code);
  await page.getByRole("button", { name: "Register till" }).click();
  await page.getByRole("button", { name: staff }).click();
  for (const d of pin) await page.getByRole("button", { name: d, exact: true }).click();
  await page.getByRole("button", { name: "OK" }).click();
  await expect(page.getByPlaceholder(/Scan or type/)).toBeVisible();
}

test("till: register with the setup code, unlock with a PIN, sell by barcode, take cash, and the sale reaches the server", async ({ page }) => {
  const problems = watchProblems(page);
  page.on("response", async (r) => { if (/sync\/upload/.test(r.url())) console.log("UPLOAD", r.status(), (await r.text().catch(() => "")).slice(0, 300)); });
  await registerAndUnlock(page, /Thandi/, "1234");
  await expect(page.getByText(/Updated \d\d:\d\d/)).toBeVisible();   // the first download from the real API worked

  const box = page.getByPlaceholder(/Scan or type/);
  for (const code of ["6001234500011", "6001234500066", "6001234500066"]) { await box.fill(code); await box.press("Enter"); }
  await expect(page.getByText("White bread 700g")).toBeVisible();
  await expect(page.getByText("R 46,97").first()).toBeVisible();   // 18,99 + 2 x 13,99 = 46,97, VAT included

  await page.getByRole("button", { name: /^Pay/ }).click();
  await page.getByRole("button", { name: "Cash", exact: true }).click();
  await page.getByRole("button", { name: "Exact" }).click();   // the quick-cash buttons finish the sale
  await expect(page.getByText(/Sale saved/)).toBeVisible();

  // The queue is sent in the background: the number waiting goes away, and the till has no refused sales.
  await expect(page.getByText(/sales waiting to send/)).toHaveCount(0, { timeout: 30_000 });
  await expect(page.getByText(/sales refused/)).toHaveCount(0);
  expect(problems.list).toEqual([]);
});

test("till: a wrong PIN is refused and a manager sees every tab, each of which opens without errors", async ({ page }) => {
  const problems = watchProblems(page);
  const { shop, till, code } = await seededTill();
  await page.goto("http://localhost:5173");
  await page.getByLabel("Shop number").fill(shop);
  await page.getByLabel("Till number").fill(till);
  await page.getByLabel("Setup code").fill(code);
  await page.getByRole("button", { name: "Register till" }).click();
  await page.getByRole("button", { name: /Sipho/ }).click();
  for (const d of "0000") await page.getByRole("button", { name: d, exact: true }).click();
  await page.getByRole("button", { name: "OK" }).click();
  await expect(page.getByText(/That PIN is not right/)).toBeVisible();
  for (const d of "4321") await page.getByRole("button", { name: d, exact: true }).click();
  await page.getByRole("button", { name: "OK" }).click();
  await expect(page.getByPlaceholder(/Scan or type/)).toBeVisible();

  const tabs = (await page.locator("nav button").allInnerTexts()).map((t) => t.trim());
  expect(tabs.length).toBeGreaterThan(3);
  for (const tab of tabs) {
    await page.locator("nav").getByRole("button", { name: tab, exact: true }).click();
    await page.waitForTimeout(600);
    for (const inner of await tabBar(page)) {
      const b = page.locator("main").getByRole("button", { name: inner, exact: true }).first();
      if (await b.isVisible() && !(await b.isDisabled())) { await b.click(); await page.waitForTimeout(400); }
    }
  }
  expect(problems.list).toEqual([]);
});

test("till: fits a phone", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await registerAndUnlock(page, /Thandi/, "1234");
  const box = page.getByPlaceholder(/Scan or type/);
  await box.fill("6001234500066"); await box.press("Enter");
  await expectNoSidewaysScroll(page, "the sell screen");
  for (const tab of (await page.locator("nav button").allInnerTexts()).map((t) => t.trim())) {
    await page.locator("nav").getByRole("button", { name: tab, exact: true }).click();
    await page.waitForTimeout(400);
    await expectNoSidewaysScroll(page, `the ${tab} tab`);
  }
});
