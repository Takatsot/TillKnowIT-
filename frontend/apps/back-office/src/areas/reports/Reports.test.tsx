// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Reports } from "./Reports";

afterEach(cleanup);
vi.setConfig({ testTimeout: 30000 });

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const period = { days: 1, salesCents: 10000, profitCents: 2500, customers: 4, averageSaleCents: 2500 };
const insights = {
  today: period, week: period, month: period,
  bestSellers: [{ productId: "pr1", name: "Bread", units: 40, revenueCents: 60000, profitCents: 9000 }],
  runningLow: [{ productId: "pr2", name: "Milk 1L", onHand: 2, daysLeft: 1, expiryDate: null }],
  notSelling: [], expiring: [],
};
const branches = [
  { shopId: "s1", name: "Head shop", isHead: true },
  { shopId: "s2", name: "Soweto branch", isHead: false },
];

function routes(over: Record<string, unknown> = {}) {
  return vi.fn(async (path: string) => {
    if (path in over) return over[path];
    if (path === "/shops/s1/insights") return ok(insights);
    if (path === "/organisations/s1/branches") return ok(branches);
    return fail("test.unexpected", path);
  });
}

function mount(api: Partial<Api>, save = vi.fn()) {
  render(<SessionProvider api={api as Api} initial={session}><Reports save={save} /></SessionProvider>);
  return save;
}

describe("Reports", () => {
  it("shows the owner's insights", async () => {
    mount({ get: routes() as never });

    expect(await screen.findByText("Bread")).toBeTruthy();
    expect(screen.getByText("Milk 1L")).toBeTruthy();
    expect(screen.getByText("Today")).toBeTruthy();
    expect(screen.getAllByText("Nothing to show.").length).toBe(2);
  });

  it("downloads the chosen report for all shops and saves the file", async () => {
    const file = { blob: new Blob(["x"]), fileName: "cash-ups.pdf" };
    const download = vi.fn(async () => ok(file));
    const save = mount({ get: routes() as never, download: download as never });
    const user = userEvent.setup();
    await screen.findByText("Bread");
    await screen.findByRole("option", { name: "Soweto branch" });

    await user.selectOptions(screen.getByLabelText("Report"), "cash-ups");
    await user.selectOptions(screen.getByLabelText("Format"), "pdf");
    await user.selectOptions(screen.getByLabelText("Shops to include"), "all");
    await user.type(screen.getByLabelText("From"), "2026-10-01");
    await user.type(screen.getByLabelText("To"), "2026-10-28");
    await user.click(screen.getByRole("button", { name: "Download report" }));

    await waitFor(() => expect(save).toHaveBeenCalledWith(file));
    expect(download).toHaveBeenCalledWith("/shops/s1/reports/cash-ups", { from: "2026-10-01", to: "2026-10-28", format: "pdf", branch: "all", callerPersonId: "p1" });
    expect(screen.getByText("Your report cash-ups.pdf was downloaded.")).toBeTruthy();
  });

  it("checks the dates before asking the server", async () => {
    const download = vi.fn();
    mount({ get: routes() as never, download: download as never });
    const user = userEvent.setup();
    await screen.findByText("Bread");

    await user.type(screen.getByLabelText("From"), "2026-10-20");
    await user.type(screen.getByLabelText("To"), "2026-10-01");
    await user.click(screen.getByRole("button", { name: "Download report" }));

    expect(screen.getByText("The start date must not be after the end date.")).toBeTruthy();
    expect(download).not.toHaveBeenCalled();
  });

  it("shows the server's refusal and still offers this shop when branches are not available", async () => {
    const download = vi.fn(async () => fail("report.range_too_large", "A report covers at most 366 days."));
    const save = mount({ get: routes({ "/organisations/s1/branches": fail("plan.feature_not_included", "Pro plan") }) as never, download: download as never });
    const user = userEvent.setup();
    await screen.findByText("Bread");

    expect(screen.queryByRole("option", { name: "All shops in the business" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Download report" }));

    expect((await screen.findByRole("alert")).textContent).toBe("A report covers at most 366 days.");
    expect(save).not.toHaveBeenCalled();
    expect(download).toHaveBeenCalledWith("/shops/s1/reports/sales-by-day", { from: "", to: "", format: "csv", branch: undefined, callerPersonId: "p1" });
  });
});
