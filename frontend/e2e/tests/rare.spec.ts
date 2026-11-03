import { expect, test } from "@playwright/test";
import { PEOPLE, registerTill, startSignedIn, watchProblems } from "./helpers";

/** Screens that are rarely opened but must work: the supplier's price-list import. Each run uses barcodes of its own so it can be repeated. */
const stamp = () => String(Date.now()).slice(-7);

test("supplier portal: a price list is previewed, then saved, and the products appear in the catalogue", async ({ page }) => {
  const problems = watchProblems(page);
  await startSignedIn(page, PEOPLE.supplier);
  await page.goto("http://localhost:5175");
  const menu = page.locator("nav[aria-label]").first();
  await menu.getByRole("button", { name: "Catalogue", exact: true }).click();
  await page.getByRole("tab", { name: "Import" }).or(page.getByRole("button", { name: "Import", exact: true })).first().click();

  const n = stamp();
  const csv = `Barcode,Name,Units per case,Case price\n7${n}001,Import Test Rice ${n},12,R 180.00\n7${n}002,Import Test Beans ${n},24,R 240.00\n`;
  await page.locator("input[type=file]").setInputFiles({ name: "prices.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await expect(page.getByText(/Preview: 2 new/)).toBeVisible();
  await expect(page.getByText(`Import Test Rice ${n}`)).toBeVisible();

  await page.getByRole("button", { name: "Save the import" }).click();
  await expect(page.getByText(/Saved: 2 new/)).toBeVisible();

  await page.getByRole("tab", { name: "Products" }).or(page.getByRole("button", { name: "Products", exact: true })).first().click();
  await expect(page.getByText(`Import Test Beans ${n}`)).toBeVisible();
  expect(problems.list).toEqual([]);
});

test("reports: after a sale on the till, today's figures and best sellers show it, and a sales report downloads", async ({ page }) => {
  const problems = watchProblems(page);
  await registerTill(page);
  const box = page.getByPlaceholder(/Scan or type/);
  await box.fill("6001234500011"); await box.press("Enter");
  await page.getByRole("button", { name: /^Pay/ }).click();
  await page.getByRole("button", { name: "Cash", exact: true }).click();
  await page.getByRole("button", { name: "Exact" }).click();
  await expect(page.getByText(/Sale saved/)).toBeVisible();
  await expect(page.getByText(/sales waiting to send/)).toHaveCount(0, { timeout: 30_000 });

  await startSignedIn(page, PEOPLE.owner);
  await page.goto("http://localhost:5174");
  await page.locator("nav[aria-label]").first().getByRole("button", { name: "Reports", exact: true }).click();
  await expect(page.getByText("How the shop is doing")).toBeVisible();
  await expect(page.getByText("Best sellers")).toBeVisible();
  await expect(page.locator("main")).toContainText("White bread 700g");

  await page.getByLabel("Report", { exact: true }).selectOption({ label: "Sales by product" });
  const [file] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Download report" }).click()]);
  expect(file.suggestedFilename()).toMatch(/\.csv$/);
  await expect(page.getByText(/was downloaded/)).toBeVisible();
  expect(problems.list).toEqual([]);
});
