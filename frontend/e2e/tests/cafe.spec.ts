import { expect, test, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { API, PEOPLE, sessionFor, startSignedIn, tabBar, walkTabs, watchProblems } from "./helpers";

const SEED = path.join(import.meta.dirname, "..", "..", "..", "backend", "src", "Host", "TillKnowIT.Api", "dev-seed.md");

/** Puts the demo shop back on the plain spaza pack, so the other tests see what they expect. */
async function backToSpaza() {
  const md = fs.readFileSync(SEED, "utf8");
  const shop = md.match(/Shop: .*?id `([^`]+)`/)![1];
  const owner = await sessionFor(PEOPLE.owner);
  await fetch(`${API}/api/v1/shops/${shop}/business-pack`, {
    method: "PUT", headers: { "content-type": "application/json", authorization: `Bearer ${owner.accessToken}` }, body: JSON.stringify({ callerPersonId: owner.personId, packCode: "spaza" }),
  });
}

async function openSection(page: Page, name: string) {
  await page.locator("nav[aria-label]").first().getByRole("button", { name, exact: true }).click();
  await page.waitForTimeout(600);
}

async function registerCafeTill(page: Page) {
    const md = fs.readFileSync(SEED, "utf8");
    const shop = md.match(/Shop: .*?id `([^`]+)`/)![1];
    const till = md.match(/Till: .*?id `([^`]+)`/)![1];
    const owner = await sessionFor(PEOPLE.owner);
    const headers = { authorization: `Bearer ${owner.accessToken}` };
    const reset = await fetch(`${API}/api/v1/devices/${till}/reset-credential?callerPersonId=${owner.personId}`, { method: "POST", headers });
    const code = ((await reset.json()) as { enrolmentCode: string }).enrolmentCode;
    await fetch(`${API}/api/v1/devices/${till}/approve?callerPersonId=${owner.personId}`, { method: "POST", headers });

    await page.goto("http://localhost:5173");
    await page.getByLabel("Shop number").fill(shop);
    await page.getByLabel("Till number").fill(till);
    await page.getByLabel("Setup code").fill(code);
    await page.getByRole("button", { name: "Register till" }).click();
    await page.getByRole("button", { name: /Sipho/ }).click();
    for (const d of "4321") await page.getByRole("button", { name: d, exact: true }).click();
    await page.getByRole("button", { name: "OK" }).click();
    await expect(page.getByPlaceholder(/Scan or type/)).toBeVisible();
    await expect(page.getByText(/Updated \d\d:\d\d/)).toBeVisible();

}

test.describe.serial("a cafe: the business type switches on tables, tickets, the kitchen and more", () => {
  test.afterAll(backToSpaza);

  test("the owner chooses the cafe type in the back office, and the gated screens open instead of saying they are off", async ({ page }) => {
    const problems = watchProblems(page);
    await startSignedIn(page, PEOPLE.owner);
    await page.goto("http://localhost:5174");
    await openSection(page, "Business type");
    await page.getByRole("button", { name: /^Choose Cafe/ }).click();
    await expect(page.getByText(/Your shop now uses Cafe/)).toBeVisible();

    // The screens that said "not switched on" for a spaza shop now work.
    for (const section of ["Promotions", "Modifiers, variants and tables", "Services and appointments", "Customers and loyalty"]) {
      await openSection(page, section);
      await walkTabs(page, `back office (cafe) > ${section}`, problems);
    }
    expect(problems.list).toEqual([]);
  });

  test("the till of a cafe shows the cafe tabs and every one opens", async ({ page }) => {
    const problems = watchProblems(page);
    await registerCafeTill(page);
    const tabs = (await page.locator("nav button").allInnerTexts()).map((t) => t.trim());
    console.log("cafe till tabs:", tabs.join(", "));
    expect(tabs.length, "a cafe till has more tabs than a spaza till").toBeGreaterThan(6);
    for (const tab of tabs) {
      const b = page.locator("nav").getByRole("button", { name: tab, exact: true });
      if (await b.isDisabled()) continue;   // some tabs need the till to be online
      await b.click();
      await page.waitForTimeout(700);
      for (const inner of await tabBar(page)) {
        const i = page.locator("main").getByRole("button", { name: inner, exact: true }).first();
        if (await i.isVisible() && !(await i.isDisabled())) { await i.click(); await page.waitForTimeout(400); }
      }
    }
    expect(problems.list).toEqual([]);
  });

  test("tickets: open one with an item, move it to the main sale, and take payment for another", async ({ page }) => {
    const problems = watchProblems(page);
    await registerCafeTill(page);
    await page.locator("nav").getByRole("button", { name: "Tables", exact: true }).click();

    // Open a ticket without a table, with one item.
    await page.getByRole("button", { name: "New ticket without a table" }).click();
    await page.getByLabel("Ticket name or number").fill("Takeaway 12");
    await page.getByRole("textbox", { name: "Search for an item" }).fill("bread");
    await page.getByRole("button", { name: "Add White bread 700g" }).click();
    await page.getByRole("button", { name: "Open ticket" }).click();
    await expect(page.getByText(/Ticket Takeaway 12/)).toBeVisible();

    // Move it to the main sale: the goods arrive on the Sell tab and the ticket is gone.
    await page.getByRole("button", { name: "Move to the sale on the till" }).click();
    const line = page.locator("li:visible", { hasText: "White bread 700g" });
    await expect(line).toBeVisible();
    await expect(line).toContainText("R 18,99");
    await page.getByRole("button", { name: "Clear sale" }).click();
    await page.locator("nav").getByRole("button", { name: "Tables", exact: true }).click();
    await page.getByRole("button", { name: "Open tickets" }).click();
    await expect(page.getByText("Takeaway 12")).toHaveCount(0);

    // A second ticket is paid for in cash.
    await page.getByRole("button", { name: "New ticket without a table" }).click();
    await page.getByLabel("Ticket name or number").fill("Takeaway 13");
    await page.getByRole("textbox", { name: "Search for an item" }).fill("milk");
    await page.getByRole("button", { name: "Add Full cream milk 1L" }).click();
    await page.getByRole("button", { name: "Open ticket" }).click();
    await page.getByRole("button", { name: "Take payment" }).click();
    await page.getByRole("button", { name: /Cash/ }).first().click();
    expect(problems.list).toEqual([]);
  });
});
