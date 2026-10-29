// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Suppliers } from "./Suppliers";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const acme = {
  id: "s1", name: "Acme Wholesale", status: "pending", rejectionReason: null, minOrderCents: 50000, deliveryFeeCents: 0, paymentTerms: "both",
  areas: [{ municipalityCode: "jhb", days: ["Mon"] }], verified: false,
};

function routes(over: Record<string, unknown> = {}) {
  return vi.fn(async (path: string) => {
    if (path in over) return over[path];
    if (path === "/admin/suppliers/pending") return ok([acme]);
    if (path === "/admin/marketplace/settings") return ok({ "ranking.weight_cost": "2" });
    return fail("test.unexpected", path);
  });
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Suppliers /></SessionProvider>);
}

describe("Suppliers", () => {
  it("lists pending suppliers and the ranking settings with their ranges", async () => {
    const get = routes();
    mount({ get: get as never });

    expect(await screen.findByText("Acme Wholesale")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/admin/suppliers/pending", { adminPersonId: "p1" });
    expect(((await screen.findByLabelText("Weight of cost")) as HTMLInputElement).value).toBe("2");
    expect(((await screen.findByLabelText("Sponsored boost (%)")) as HTMLInputElement).value).toBe("0");
    expect(screen.getByText(/Allowed range: 0 to 20/)).toBeTruthy();
  });

  it("verifies with what was checked and reloads the list", async () => {
    const get = routes();
    const post = vi.fn(async () => ok(undefined));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Acme Wholesale");

    await user.click(screen.getByRole("button", { name: "Review Acme Wholesale" }));
    await user.click(screen.getByLabelText("Business registration checked"));
    await user.click(screen.getByLabelText("Prices reviewed"));
    await user.click(screen.getByRole("button", { name: "Verify supplier" }));

    await waitFor(() => expect(screen.getByText("Acme Wholesale was verified.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/suppliers/s1/verify", { adminPersonId: "p1", checklist: ["registration_checked", "prices_reviewed"] });
    expect(get.mock.calls.filter((c) => c[0] === "/admin/suppliers/pending")).toHaveLength(2);
  });

  it("shows the server's message when the checklist is not complete", async () => {
    const post = vi.fn(async () => fail("supplier.checklist_incomplete", "Every check must be done before a supplier is verified."));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Acme Wholesale");

    await user.click(screen.getByRole("button", { name: "Review Acme Wholesale" }));
    await user.click(screen.getByRole("button", { name: "Verify supplier" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Every check must be done before a supplier is verified.");
  });

  it("rejects with a reason and needs the reason first", async () => {
    const post = vi.fn(async () => ok(undefined));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Acme Wholesale");

    await user.click(screen.getByRole("button", { name: "Review Acme Wholesale" }));
    expect((screen.getByRole("button", { name: "Reject supplier" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByLabelText("Reason for rejecting"), "No registration");
    await user.click(screen.getByRole("button", { name: "Reject supplier" }));

    await waitFor(() => expect(screen.getByText("Acme Wholesale was rejected.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/suppliers/s1/reject", { adminPersonId: "p1", reason: "No registration" });
  });

  it("shows the empty state", async () => {
    mount({ get: routes({ "/admin/suppliers/pending": ok([]) }) as never });

    expect(await screen.findByText("No suppliers are waiting for approval.")).toBeTruthy();
  });

  it("saves a setting, and refuses a value outside the range before asking the server", async () => {
    const put = vi.fn(async () => ok(undefined));
    mount({ get: routes() as never, put: put as never });
    const user = userEvent.setup();
    const boost = await screen.findByLabelText("Sponsored boost (%)");

    await user.clear(boost);
    await user.type(boost, "25");
    await user.click(screen.getByRole("button", { name: "Save Sponsored boost (%)" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Allowed range is 0 to 20.");
    expect(put).not.toHaveBeenCalled();

    await user.clear(boost);
    await user.type(boost, "5");
    await user.click(screen.getByRole("button", { name: "Save Sponsored boost (%)" }));
    await waitFor(() => expect(screen.getByText("Sponsored boost (%) was saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/admin/marketplace/settings/ranking.sponsored_boost_percent", { adminPersonId: "p1", value: "5" });
  });

  it("shows the server's refusal for a setting and for the list", async () => {
    mount({ get: routes({ "/admin/marketplace/settings": fail("permission.denied", "Only a platform admin can see marketplace settings.") }) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("Only a platform admin can see marketplace settings.");
  });
});
