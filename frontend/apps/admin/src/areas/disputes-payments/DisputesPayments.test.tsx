// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DisputesPayments } from "./DisputesPayments";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const dispute = {
  id: "d1", orderId: "o-77", shopId: "s", supplierId: "u", raisedBy: "shop", reason: "Half the order never arrived", status: "open", outcome: null, note: null,
  raisedAt: "2026-10-27T08:00:00Z", resolvedAt: null,
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><DisputesPayments /></SessionProvider>);
}

describe("DisputesPayments", () => {
  it("lists open disputes and resolves one with an outcome and note", async () => {
    const get = vi.fn(async () => ok([dispute]));
    const post = vi.fn(async () => ok({ ...dispute, status: "resolved" }));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    expect(await screen.findByText("Half the order never arrived", { selector: "td" })).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Resolve o-77" }));
    await user.selectOptions(screen.getByLabelText("Outcome"), "refund_shop");
    await user.type(screen.getByLabelText("Note (optional)"), "Photos checked");
    await user.click(screen.getByRole("button", { name: "Resolve dispute" }));

    await waitFor(() => expect(screen.getByText("The dispute on order o-77 was resolved.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/disputes/d1/resolve", { callerPersonId: "p1", outcome: "refund_shop", note: "Photos checked" });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("shows the server's message when resolving is refused", async () => {
    const post = vi.fn(async () => fail("dispute.already_resolved", "This dispute is already resolved."));
    mount({ get: vi.fn(async () => ok([dispute])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("o-77");

    await user.click(screen.getByRole("button", { name: "Resolve o-77" }));
    await user.click(screen.getByRole("button", { name: "Resolve dispute" }));

    expect((await screen.findByRole("alert")).textContent).toBe("This dispute is already resolved.");
  });

  it("shows the empty state", async () => {
    mount({ get: vi.fn(async () => ok([])) as never });

    expect(await screen.findByText("There are no open disputes.")).toBeTruthy();
  });

  it("runs the reconciliation for a day and shows the mismatches", async () => {
    const post = vi.fn(async () => ok({ day: "2026-10-27", matched: 12, mismatches: [{ partnerRef: "ref-9", kind: "missing_locally", detail: "Partner has R100.00 we do not" }] }));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("There are no open disputes.");

    fireEvent.change(screen.getByLabelText("Day"), { target: { value: "2026-10-27" } });
    await user.click(screen.getByRole("button", { name: "Run reconciliation" }));

    expect(await screen.findByText("Report for 2026-10-27")).toBeTruthy();
    expect(screen.getByText(/12 payments matched\. 1 do not match\./)).toBeTruthy();
    expect(screen.getByText("ref-9")).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/admin/payments/reconcile", undefined, { day: "2026-10-27", callerPersonId: "p1" });
  });

  it("says when nothing is out of step and shows a refusal", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(ok({ day: "2026-10-27", matched: 3, mismatches: [] }))
      .mockResolvedValueOnce(fail("payment.partner_not_connected", "The payment partner is not connected yet."));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("There are no open disputes.");

    await user.click(screen.getByRole("button", { name: "Run reconciliation" }));
    expect(await screen.findByText(/Nothing is out of step/)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Run reconciliation" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The payment partner is not connected yet.");
  });
});
