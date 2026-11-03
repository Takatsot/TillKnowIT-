import { defineConfig } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";

/** The browser: PW_CHROME if set, else the Chromium Playwright already downloaded, else Playwright's own default. */
function chromePath(): string | undefined {
  if (process.env["PW_CHROME"]) return process.env["PW_CHROME"];
  const cache = path.join(process.env["USERPROFILE"] ?? process.env["HOME"] ?? "", "AppData/Local/ms-playwright");
  if (!fs.existsSync(cache)) return undefined;
  const dir = fs.readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().at(-1);
  const exe = dir ? path.join(cache, dir, "chrome-win64", "chrome.exe") : "";
  return exe && fs.existsSync(exe) ? exe : undefined;
}

const apps = [
  ["till", 5173], ["back-office", 5174], ["supplier-portal", 5175], ["admin", 5176], ["parent-app", 5177], ["insights-portal", 5178],
] as const;

export default defineConfig({
  testDir: "./tests",
  timeout: 120_000,
  expect: { timeout: 10_000 },
  workers: 1,
  reporter: [["list"], ["html", { open: "never" }]],
  use: { launchOptions: { executablePath: chromePath() }, screenshot: "only-on-failure", trace: "retain-on-failure" },
  globalSetup: "./global-setup.ts",
  // The apps are started for you if they are not already running. The API is not: it needs SQL Server (see README.md).
  webServer: apps.map(([name, port]) => ({
    command: `npm run dev -w @tillknowit/${name}`,
    cwd: "..",
    url: `http://localhost:${port}`,
    reuseExistingServer: true,
    timeout: 120_000,
  })),
});
