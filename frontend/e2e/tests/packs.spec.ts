import { expect, test } from "@playwright/test";
import { PEOPLE, registerTill, setPack, startSignedIn, walkTabs, watchProblems } from "./helpers";

/** What each business type adds to the till (tab names), and the back-office screens that stop saying "not switched on". */
const packs = [
  { code: "butchery", tabs: [] as string[], note: "sells by weight, no extra tabs" },
  { code: "clothing_hardware", tabs: ["Quotes", "Lay-by", "Serial numbers"], note: "quotes, lay-by, variants and serial numbers" },
  { code: "salon", tabs: ["Appointments"], note: "services and appointments" },
] as const;

test.describe.serial("every business type opens cleanly", () => {
  test.afterAll(async () => setPack("spaza"));

  for (const pack of packs) {
    test(`${pack.code}: ${pack.note}`, async ({ page }) => {
      await setPack(pack.code);
      const problems = watchProblems(page);
      await registerTill(page);

      const tabs = await (async () => {
        const names = (await page.locator("nav button").allInnerTexts()).map((t) => t.trim());
        return names;
      })();
      console.log(`${pack.code} till tabs:`, tabs.join(", "));
      for (const wanted of pack.tabs) expect(tabs.join("|"), `${pack.code} should have a ${wanted} tab`).toContain(wanted);

      // Every tab and the parts inside it.
      for (const tab of tabs) {
        const b = page.locator("nav").getByRole("button", { name: tab, exact: true });
        if (await b.isDisabled()) continue;
        await b.click();
        await page.waitForTimeout(600);
        await expect(page.locator("main")).not.toBeEmpty();
      }
      expect(problems.list).toEqual([]);
    });

    test(`${pack.code}: the back office screens it unlocks`, async ({ page }) => {
      await setPack(pack.code);
      const problems = watchProblems(page);
      await startSignedIn(page, PEOPLE.owner);
      await page.goto("http://localhost:5174");
      const menu = page.locator("nav[aria-label]").first();
      for (const section of ["Promotions", "Modifiers, variants and tables", "Services and appointments", "Customers and loyalty"]) {
        await menu.getByRole("button", { name: section, exact: true }).click();
        await page.waitForTimeout(600);
        await walkTabs(page, `back office (${pack.code}) > ${section}`, problems);
      }
    });
  }
});
