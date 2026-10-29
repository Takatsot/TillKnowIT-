// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Catalogue } from "./Catalogue";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const cola = { id: "g1", barcode: "6001234567890", name: "Cola 2l", size: "2l", categoryCode: "drinks", suggestedAt: "2026-10-27T08:00:00Z" };
const drinks = { type: "categories", code: "drinks", names: { en: "Drinks" }, isActive: true, sortOrder: 1 };
const snacks = { type: "categories", code: "snacks", names: { en: "Snacks" }, isActive: true, sortOrder: 2 };

function getter(over: Record<string, unknown> = {}) {
  return vi.fn(async (path: string) => {
    if (path in over) return over[path];
    if (path === "/admin/catalogue/suggestions") return ok([cola]);
    if (path === "/lookups/categories") return ok([drinks, snacks]);
    return fail("test.unexpected", path);
  });
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Catalogue /></SessionProvider>);
}

describe("Catalogue", () => {
  it("approves a suggestion with a corrected category and reloads", async () => {
    const get = getter();
    const post = vi.fn(async () => ok(undefined));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Cola 2l");

    await user.click(screen.getByRole("button", { name: "Approve Cola 2l" }));
    await user.selectOptions(screen.getByLabelText("Category"), "snacks");
    await user.click(screen.getByRole("button", { name: "Approve product" }));

    await waitFor(() => expect(screen.getByText("Cola 2l was approved.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/catalogue/suggestions/g1/approve", { adminPersonId: "p1", name: null, size: null, categoryCode: "snacks" });
    expect(get.mock.calls.filter((c) => c[0] === "/admin/catalogue/suggestions")).toHaveLength(2);
  });

  it("rejects with a reason and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("catalogue.already_reviewed", "This suggestion has already been reviewed."));
    mount({ get: getter() as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Cola 2l");

    await user.click(screen.getByRole("button", { name: "Reject Cola 2l" }));
    await user.type(screen.getByLabelText("Reason for rejecting"), "Not a product");
    await user.click(screen.getByRole("button", { name: "Reject product" }));

    expect((await screen.findByRole("alert")).textContent).toBe("This suggestion has already been reviewed.");
    expect(post).toHaveBeenCalledWith("/admin/catalogue/suggestions/g1/reject", { adminPersonId: "p1", reason: "Not a product" });
  });

  it("finds the existing product by barcode and merges into it", async () => {
    const get = getter({ "/catalogue/products": ok({ id: "prod1", barcode: "6009999999999", name: "Cola 2 litre" }) });
    const post = vi.fn(async () => ok(undefined));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Cola 2l");

    await user.click(screen.getByRole("button", { name: "Merge Cola 2l" }));
    await user.type(screen.getByLabelText("Barcode of the product we already have"), "6009999999999");
    await user.click(screen.getByRole("button", { name: "Find product" }));
    expect(await screen.findByText("Found: Cola 2 litre (6009999999999)")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Merge into this product" }));

    await waitFor(() => expect(screen.getByText("Cola 2l was merged.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/catalogue/suggestions/g1/merge", { adminPersonId: "p1", intoProductId: "prod1" });
    expect(get).toHaveBeenCalledWith("/catalogue/products", { barcode: "6009999999999" });
  });

  it("shows the empty state and a list error", async () => {
    mount({ get: getter({ "/admin/catalogue/suggestions": ok([]) }) as never });
    expect(await screen.findByText("No suggested products are waiting for review.")).toBeTruthy();
    cleanup();

    mount({ get: getter({ "/admin/catalogue/suggestions": fail("permission.denied", "Only a platform admin can review suggestions.") }) as never });
    expect((await screen.findByRole("alert")).textContent).toBe("Only a platform admin can review suggestions.");
  });

  it("lists a platform lookup and saves an English name change", async () => {
    const get = getter();
    const put = vi.fn(async () => ok({ ...drinks, names: { en: "Cold drinks" } }));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Cola 2l");

    expect(await screen.findByText("Snacks")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/lookups/categories", { includeInactive: true });
    await user.click(screen.getByRole("button", { name: "Edit drinks" }));
    const english = screen.getByLabelText("English name");
    await user.clear(english);
    await user.type(english, "Cold drinks");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("drinks was saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/admin/lookups/categories/drinks", { adminPersonId: "p1", names: { en: "Cold drinks" }, isActive: true, sortOrder: 1 });
  });
});
