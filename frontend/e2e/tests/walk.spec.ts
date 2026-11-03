import { expect, test } from "@playwright/test";
import { expectNoSidewaysScroll, PEOPLE, startSignedIn, walkTabs, watchProblems } from "./helpers";

/** Every menu section of every app, and every tab inside it: nothing may throw, draw blank, or (on a phone) run off the side. */
const apps = [
  { name: "back office", url: "http://localhost:5174", phone: PEOPLE.owner },
  { name: "supplier portal", url: "http://localhost:5175", phone: PEOPLE.supplier },
  { name: "admin", url: "http://localhost:5176", phone: PEOPLE.admin },
  { name: "parent app", url: "http://localhost:5177", phone: PEOPLE.parent },
  { name: "insights portal", url: "http://localhost:5178", phone: PEOPLE.supplier },
] as const;

for (const app of apps) {
  for (const phone of [false, true]) {
    test(`${app.name}: every section and tab${phone ? " on a phone" : ""}`, async ({ page }) => {
      await page.setViewportSize(phone ? { width: 390, height: 844 } : { width: 1360, height: 900 });
      const problems = watchProblems(page);
      await startSignedIn(page, app.phone);
      await page.goto(app.url);

      const menu = page.locator("nav[aria-label]").first();
      await expect(menu).toBeVisible();
      const sections = (await menu.locator("button").allInnerTexts()).map((t) => t.trim());
      expect(sections.length, "the menu has sections").toBeGreaterThan(0);

      let tabsClicked = 0;
      for (const section of sections) {
        await menu.getByRole("button", { name: section, exact: true }).click();
        await page.waitForTimeout(700);
        await expect(page.locator("main")).not.toBeEmpty();
        if (phone) await expectNoSidewaysScroll(page, section);
        tabsClicked += await walkTabs(page, `${app.name} > ${section}`, problems, phone);
      }
      console.log(`${app.name}${phone ? " (phone)" : ""}: ${sections.length} sections, ${tabsClicked} tabs`);
      if (app.name === "admin") expect(tabsClicked, "the schools tabs were found").toBeGreaterThan(3);
      if (app.name === "back office") expect(tabsClicked, "the tab bars were found").toBeGreaterThan(10);
    });
  }
}

test("signing in through the screen works and shows the code in development", async ({ page }) => {
  // Uses the sign-in screen itself once (a code is allowed five times an hour per number, so this uses the school admin who is not used elsewhere).
  await page.goto("http://localhost:5177");
  await page.getByLabel("Your cell phone number").fill("0825550007");
  await page.getByRole("button", { name: "Send me a code" }).click();
  const hint = page.getByText(/so you can test without a phone/);
  await expect(hint).toBeVisible();
  const code = (await hint.innerText()).match(/: (\d{4,8})/)?.[1] ?? "";
  await page.getByLabel("The code we sent you").fill(code);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("heading", { name: "My children" }).first()).toBeVisible();
});

test("admin: opening a school, its tabs, and a child's cards", async ({ page }) => {
  const problems = watchProblems(page);
  await startSignedIn(page, PEOPLE.admin);
  await page.goto("http://localhost:5176");
  await page.getByRole("button", { name: "Schools and wallet", exact: true }).click();
  await page.getByLabel("School", { exact: true }).selectOption({ label: "Sunrise Primary" });
  await page.getByRole("button", { name: "Open school" }).click();
  await expect(page.getByText("Working on Sunrise Primary.")).toBeVisible();
  const n = await walkTabs(page, "admin > school", problems);
  expect(n).toBeGreaterThan(4);

  await page.getByRole("tab", { name: "Holders" }).click();
  await page.getByRole("button", { name: "Cards for Themba Khumalo" }).click();
  // A card can only be linked once, so each run uses a number of its own.
  await page.getByLabel("Card number").fill(`04${Date.now().toString(16).toUpperCase()}`);
  const name = `Wristband ${Date.now() % 100000}`;
  await page.getByLabel("Card name").fill(name);
  await page.getByRole("button", { name: "Link card" }).click();
  await expect(page.getByText("The card is now linked to Themba Khumalo.")).toBeVisible();
  await expect(page.getByText(name).first()).toBeVisible();
  expect(problems.list).toEqual([]);
});
