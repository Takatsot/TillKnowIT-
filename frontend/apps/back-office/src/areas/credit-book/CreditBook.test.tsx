// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CreditBook } from "./CreditBook";

afterEach(cleanup);
vi.setConfig({ testTimeout: 30000 });

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const accounts = [
  { id: "a1", name: "Mama Zodwa", phone: "0821234567", creditLimitCents: 50000, balanceCents: 60000, overLimit: true },
  { id: "a2", name: "Bra Joe", phone: null, creditLimitCents: null, balanceCents: 1000, overLimit: false },
];

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><CreditBook /></SessionProvider>);
}

describe("CreditBook", () => {
  it("lists accounts and warns about customers over their limit", async () => {
    mount({ get: vi.fn(async () => ok(accounts)) as never });

    expect(await screen.findByText("Mama Zodwa")).toBeTruthy();
    expect(screen.getByText("Over limit")).toBeTruthy();
    expect(screen.getByText("No limit")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("1 customers are over their credit limit");
  });

  it("shows the entries of one customer", async () => {
    const get = vi.fn(async (path: string) =>
      path.endsWith("/entries") ? ok([{ id: "e1", type: "payment", amountCents: -20000, saleId: null, paymentMethodCode: "cash", createdAt: "2026-10-01T08:00:00Z" }]) : ok(accounts));
    mount({ get: get as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "View entries for Mama Zodwa" }));

    expect(await screen.findByText("Entries for Mama Zodwa")).toBeTruthy();
    expect(await screen.findByText("Payment")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/shops/s1/credit-accounts/a1/entries");
  });

  it("shows the empty state", async () => {
    mount({ get: vi.fn(async () => ok([])) as never });
    expect(await screen.findByText("No customers on credit yet.")).toBeTruthy();
  });

  it("shows the server's message when the list cannot be loaded", async () => {
    mount({ get: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) as never });
    await waitFor(() => expect(screen.getByRole("alert").textContent).toBe("We cannot reach the server."));
  });
});
