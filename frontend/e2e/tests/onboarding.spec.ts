import { expect, test } from "@playwright/test";
import { PEOPLE, startSignedIn, watchProblems } from "./helpers";

/** A new owner signs up and makes a shop; a field agent sets it up and verifies it; the owner sees it verified. Each run uses a new phone number and shop name. */
test("onboarding: a new owner creates a shop, the field agent completes the checklist and verifies it, and the owner sees it verified", async ({ browser }) => {
  const stamp = String(Date.now()).slice(-6);
  const phone = `0829${stamp}`;
  const shop = `Spaza ${stamp}`;

  // The new owner has no shop yet, so the first screen is where one is made.
  const owner = await browser.newPage();
  const ownerProblems = watchProblems(owner);
  await owner.goto("http://localhost:5174");
  await owner.getByRole("button", { name: "I am new here: create an account" }).click();
  await owner.getByLabel("Your cell phone number").fill(phone);
  await owner.getByLabel("Your name").fill(`Owner ${stamp}`);
  await owner.getByRole("button", { name: "Send me a code" }).click();
  const hint = owner.getByText(/so you can test without a phone/);
  await expect(hint).toBeVisible();
  await owner.getByLabel("The code we sent you").fill((await hint.innerText()).match(/: (\d{4,8})/)?.[1] ?? "");
  await owner.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(owner.getByRole("heading", { name: "Create your shop" })).toBeVisible();
  await owner.getByLabel(/Shop name/).fill(shop);
  await owner.getByLabel(/Your municipality/).selectOption({ label: "City of Johannesburg" });
  await owner.getByRole("button", { name: "Create my shop" }).click();
  await expect(owner.getByText("0 of 7 setup steps done")).toBeVisible();
  await expect(owner.getByText("Waiting for verification")).toBeVisible();

  // The field agent finds it, ticks off the seven steps and verifies it.
  const agent = await browser.newPage();
  const agentProblems = watchProblems(agent);
  await startSignedIn(agent, PEOPLE.agent);
  await agent.goto("http://localhost:5179");
  await agent.getByRole("button", { name: `Open ${shop}` }).click();
  await expect(agent.getByRole("button", { name: "Verify this shop" })).toBeDisabled();
  for (const step of ["Device check", "Shop details", "Document photos", "Catalogue import", "Staff PINs", "Test sale", "Training sign-off"]) {
    await agent.getByRole("button", { name: `Mark ${step} done` }).click();
    await expect(agent.getByText(`${step} is marked done.`)).toBeVisible();
  }
  await agent.getByRole("button", { name: "Verify this shop" }).click();
  await expect(agent.getByText(`${shop} is verified.`)).toBeVisible();
  await expect(agent.getByRole("button", { name: `Open ${shop}` })).toHaveCount(0);

  // Back with the owner: verified.
  await owner.reload();
  await expect(owner.getByText("7 of 7 setup steps done")).toBeVisible();
  await expect(owner.getByText("Your shop is verified. Everything is switched on.")).toBeVisible();
  expect(ownerProblems.list).toEqual([]);
  expect(agentProblems.list).toEqual([]);
});

test("onboarding: someone who is not a field agent is told so, not shown shops", async ({ page }) => {
  await startSignedIn(page, PEOPLE.owner);
  await page.goto("http://localhost:5179");
  await expect(page.getByText(/Only a field agent can see the shops waiting for setup/)).toBeVisible();
});
