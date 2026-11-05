import { expect, test, type Page } from "@playwright/test";
import { PEOPLE, registerTill, setPack, setSellingOptions, startSignedIn, watchProblems } from "./helpers";

/** Real jobs in each business type, start to finish, against the real API. Each run uses names of its own so it can be repeated. */
const stamp = () => String(Date.now()).slice(-6);

async function backOffice(page: Page, section: string) {
  await startSignedIn(page, PEOPLE.owner);
  await page.goto("http://localhost:5174");
  await page.locator("nav[aria-label]").first().getByRole("button", { name: section, exact: true }).click();
  await page.waitForTimeout(700);
}

async function addItem(page: Page, search: string, name: string) {
  await page.getByRole("textbox", { name: "Search for an item" }).fill(search);
  await page.getByRole("button", { name: `Add ${name}` }).click();
}

test.describe.serial("business type flows", () => {
  test.afterAll(async () => setPack("spaza"));

  test("cafe: the owner sets up a table, the till opens a ticket on it, and a kitchen order is sent and worked through the board", async ({ page }) => {
    await setPack("cafe");
    const problems = watchProblems(page);
    const table = `T${stamp()}`;

    // Back office: add the table to the floor plan.
    await backOffice(page, "Modifiers, variants and tables");
    await page.getByRole("button", { name: "Floor plan", exact: true }).click();
    await page.getByRole("button", { name: "Add table" }).click();
    await page.getByLabel(/^Area \d+$/).last().fill("Inside");
    await page.getByLabel(/^Table \d+$/).last().fill(table);
    await page.getByRole("button", { name: "Save floor plan" }).click();
    await expect(page.getByText(/The floor plan was saved/)).toBeVisible();

    // Till: the table is on the floor plan; open a ticket on it with an item.
    await registerTill(page);
    await page.locator("nav").getByRole("button", { name: "Tables", exact: true }).click();
    await page.getByRole("button", { name: new RegExp(`Table .*${table}`) }).click();
    await page.getByRole("textbox", { name: "Search for an item" }).fill("milk");
    await page.getByRole("button", { name: "Add Full cream milk 1L" }).click();
    await page.getByRole("button", { name: "Open ticket" }).click();
    await expect(page.getByText(new RegExp(`Ticket .*${table}`))).toBeVisible();

    // Kitchen: send an order and move it along the board.
    const order = `K${stamp()}`;
    await page.locator("nav").getByRole("button", { name: "Kitchen", exact: true }).click();
    await page.getByRole("button", { name: "Send to the kitchen", exact: true }).first().click();
    await page.getByLabel("Order number").fill(order);
    await addItem(page, "bread", "White bread 700g");
    await page.getByRole("button", { name: "Send to the kitchen" }).last().click();
    await expect(page.getByText(new RegExp(`Order ${order}.* sent to the kitchen`))).toBeVisible();
    await page.getByRole("button", { name: "Kitchen board" }).click();
    await page.getByRole("button", { name: `Start ${order}` }).click();
    await page.getByRole("button", { name: `Mark ${order} ready` }).click();
    await page.getByRole("button", { name: `Collected ${order}` }).click();
    expect(problems.list).toEqual([]);
  });

  test("clothing and hardware: a quote, and a lay-by that is started and paid into", async ({ page }) => {
    await setPack("clothing_hardware");
    const problems = watchProblems(page);
    await registerTill(page);
    const who = `Customer ${stamp()}`;

    await page.locator("nav").getByRole("button", { name: "Quotes", exact: true }).click();
    await page.getByRole("button", { name: "New quote" }).click();
    await page.getByRole("textbox", { name: "Customer name" }).fill(who);
    await addItem(page, "maize", "Maize meal 5kg");
    await page.getByRole("button", { name: "Save quote" }).click();
    await expect(page.getByText(/saved\./)).toBeVisible();
    await expect(page.getByText(who)).toBeVisible();

    // Put the quote's goods on the sale, then give the sale up unpaid: the quote can be used again.
    await page.getByRole("button", { name: /^Add Q.* to the sale on the till$/ }).first().click();
    await expect(page.locator("li:visible", { hasText: "Maize meal 5kg" })).toBeVisible();
    await page.getByRole("button", { name: "Clear sale" }).click();
    await page.locator("nav").getByRole("button", { name: "Quotes", exact: true }).click();
    await expect(page.getByRole("button", { name: /^Add Q.* to the sale on the till$/ }).first()).toBeVisible();

    await page.locator("nav").getByRole("button", { name: "Lay-by", exact: true }).click();
    await page.getByRole("button", { name: "Start a lay-by" }).first().click();
    await page.getByRole("textbox", { name: "Customer name" }).fill(who);
    await addItem(page, "oil", "Sunflower oil 750ml");
    await page.getByLabel("Deposit", { exact: true }).fill("20");
    const due = new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10);
    await page.getByLabel("Pay off by").fill(due);
    await page.getByRole("button", { name: "Start lay-by" }).click();
    await expect(page.getByText(/Lay-by .* started/)).toBeVisible();
    expect(problems.list).toEqual([]);
  });

  test("salon: the owner adds a service and it is offered on the till's appointments tab", async ({ page }) => {
    await setPack("salon");
    const problems = watchProblems(page);
    const service = `Haircut ${stamp()}`;

    await backOffice(page, "Services and appointments");
    await page.getByRole("textbox", { name: "Service name" }).fill(service);
    await page.getByLabel("Duration in minutes").fill("30");
    await page.getByLabel("Price in rand").fill("120");
    await page.getByRole("button", { name: "Save service" }).click();
    await expect(page.getByText(`${service} was saved.`)).toBeVisible();

    await registerTill(page);
    await page.locator("nav").getByRole("button", { name: "Appointments", exact: true }).click();
    await expect(page.locator("main")).toContainText("Appointments");

    // Book it: the service just added, with the manager, tomorrow at ten, and it appears on that day's list.
    await page.getByRole("button", { name: "Book an appointment" }).first().click();
    await page.getByLabel("Service").selectOption({ label: new RegExp(service) as unknown as string }).catch(async () => {
      const option = page.getByLabel("Service").locator("option", { hasText: service });
      await page.getByLabel("Service").selectOption(await option.getAttribute("value") as string);
    });
    await page.getByLabel("With").selectOption({ index: 1 });   // nobody is named for the service, so the person at the till is offered
    // A day of its own each run (the same person cannot be booked twice at one time), so the test can be repeated.
    const tomorrow = new Date(Date.now() + (2 + (Date.now() % 300)) * 86_400_000).toISOString().slice(0, 10);
    await page.getByLabel("Date").fill(tomorrow);
    await page.getByLabel("Time", { exact: true }).fill("10:00");
    const customer = `Client ${stamp()}`;
    await page.getByLabel("Customer name").fill(customer);
    await page.getByRole("button", { name: "Book it" }).click();
    await expect(page.getByText(new RegExp(`Booked ${service} for ${customer}`))).toBeVisible();
    expect(problems.list).toEqual([]);
  });

  test("clothing and hardware: the owner adds a size variant to a product, and the till sells it", async ({ page }) => {
    await setPack("clothing_hardware");
    const problems = watchProblems(page);
    const label = `Large ${stamp()}`;
    const barcode = `60099${stamp()}`;

    await backOffice(page, "Modifiers, variants and tables");
    await page.getByRole("button", { name: "Variants", exact: true }).click();
    await page.getByLabel("Product", { exact: true }).selectOption({ label: "Lux soap bar" });
    await page.getByLabel(/Variant label/).fill(label);
    await page.getByLabel(/^Barcode/).fill(barcode);
    await page.getByLabel("Cost in rand").fill("8");
    await page.getByLabel("Price in rand").fill("15");
    await page.getByRole("button", { name: "Add variant" }).click();
    await expect(page.getByText(`${label} was added.`)).toBeVisible();

    await registerTill(page);
    const box = page.getByPlaceholder(/Scan or type/);
    await box.fill(barcode);
    await box.press("Enter");
    await expect(page.locator("li:visible", { hasText: "Lux soap bar" })).toContainText("R 15,00");
    expect(problems.list).toEqual([]);
  });

  test("butchery: an item is switched to sold by weight in the back office, and the till weighs it", async ({ page }) => {
    await setPack("butchery");
    const problems = watchProblems(page);
    await setSellingOptions(page, "Maize meal 5kg", { weight: true });

    try {
      await registerTill(page);
      const box = page.getByPlaceholder(/Scan or type/);
      await box.fill("6001234500042");
      await box.press("Enter");
      await page.getByRole("textbox", { name: /Weight of Maize meal 5kg in grams/ }).fill("500");
      await page.getByRole("button", { name: "Add weight" }).click();
      const line = page.locator("li:visible", { hasText: "Maize meal 5kg" });
      await expect(line).toBeVisible();
      await expect(line).toContainText("500 g");
      await expect(line).toContainText("R 37,50");   // R74,99 a kilogram, half a kilogram
    } finally {
      await setSellingOptions(page, "Maize meal 5kg", { weight: false });
    }
    expect(problems.list).toEqual([]);
  });

  test("clothing and hardware: an item that needs a serial number asks for it after the sale", async ({ page }) => {
    await setPack("clothing_hardware");
    const problems = watchProblems(page);
    await setSellingOptions(page, "Lux soap bar", { serial: true });

    try {
      await registerTill(page);
      const box = page.getByPlaceholder(/Scan or type/);
      await box.fill("6001234500080");
      await box.press("Enter");
      await page.getByRole("button", { name: /^Pay/ }).click();
      await page.getByRole("button", { name: "Cash", exact: true }).click();
      await page.getByRole("button", { name: "Exact" }).click();
      await expect(page.getByText(/These items have serial numbers/)).toBeVisible();
      await page.getByLabel(/Serial number 1 for Lux soap bar/).fill(`SN-${stamp()}`);
      await page.getByRole("button", { name: "Save serial numbers" }).click();
      await expect(page.getByText(/Serial numbers saved/)).toBeVisible();
    } finally {
      await setSellingOptions(page, "Lux soap bar", { serial: false });
    }
    expect(problems.list).toEqual([]);
  });
});
