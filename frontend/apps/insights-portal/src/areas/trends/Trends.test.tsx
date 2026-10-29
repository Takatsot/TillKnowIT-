// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Trends } from "./Trends";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "org1", deviceId: "", language: "en" };
const report = {
  area: "JHB", note: "Each figure comes from at least 10 shops. No shop is named.",
  rows: [{ weekStart: "2026-10-05", category: "Maize", units: 420, revenueBand: "R25 000 to R100 000", shops: 14 }],
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Trends /></SessionProvider>);
  return userEvent.setup({ delay: null });
}

describe("Trends", () => {
  it("always explains the 10-shop minimum", () => {
    mount({});
    expect(screen.getByText(/at least 10 shops are behind it/)).toBeTruthy();
  });

  it("asks for a report and shows weeks, units, revenue band, shops and the note", async () => {
    const get = vi.fn(async () => ok(report));
    const user = mount({ get: get as never });

    await user.type(screen.getByLabelText("Municipality code"), "JHB");
    await user.type(screen.getByLabelText("Category (optional)"), "Maize");
    await user.click(screen.getByRole("button", { name: "Show trends" }));

    expect(await screen.findByText("420")).toBeTruthy();
    expect(screen.getByText("2026-10-05")).toBeTruthy();
    expect(screen.getByText("R25 000 to R100 000")).toBeTruthy();
    expect(screen.getByText("14")).toBeTruthy();
    expect(screen.getByText(report.note)).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/insights/reports", expect.objectContaining({ personId: "p1", organisationId: "org1", audience: "supplier", area: "JHB", category: "Maize" }));
  });

  it("shows the server's note when there are no weeks", async () => {
    const note = "There is not enough data for this area yet: we only publish figures from at least 10 shops.";
    const user = mount({ get: vi.fn(async () => ok({ area: "XYZ", rows: [], note })) as never });

    await user.type(screen.getByLabelText("Municipality code"), "XYZ");
    await user.click(screen.getByRole("button", { name: "Show trends" }));

    expect(await screen.findByText("No weeks to show for this area and period.")).toBeTruthy();
    expect(screen.getByText(note)).toBeTruthy();
  });

  it("points to the plan when insights are not included", async () => {
    const user = mount({ get: vi.fn(async () => fail("plan.feature_not_included", "Market insights are not part of your plan.")) as never });

    await user.type(screen.getByLabelText("Municipality code"), "JHB");
    await user.click(screen.getByRole("button", { name: "Show trends" }));

    expect(await screen.findByText("Market insights are not in your plan")).toBeTruthy();
    expect(screen.getByText(/Open Plan in the menu/)).toBeTruthy();
    expect(screen.queryByText("Market insights are not part of your plan.")).toBeNull();
  });

  it("shows other server refusals as they are", async () => {
    const user = mount({ get: vi.fn(async () => fail("permission.denied", "You are not allowed to buy reports for this business.")) as never });

    await user.type(screen.getByLabelText("Municipality code"), "JHB");
    await user.click(screen.getByRole("button", { name: "Show trends" }));

    expect((await screen.findByText("You are not allowed to buy reports for this business.")).getAttribute("role")).toBe("alert");
  });
});
