// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, SUPPLIER } from "../../shared/testing";
import { Catalogue } from "./Catalogue";

afterEach(cleanup);

const product = { id: "sp1", barcode: "600123", name: "Maize meal 12.5kg", unitsPerCase: 2, casePriceCents: 18000, unitPriceCents: 9000, vatIncluded: true, inStock: true, expiryPolicy: null, catalogueProductId: null };
const supplier = { id: SUPPLIER, name: "Cash and Carry", minOrderCents: 50000, deliveryFeeCents: 2500, paymentTerms: "both", areas: [{ municipalityCode: "JHB", days: ["Mon"] }] };

function getter(products: unknown[] = [product]) {
  return vi.fn(async (path: string) => (path.endsWith("/products") ? ok(products) : ok(supplier)));
}

describe("Catalogue", () => {
  it("asks for the supplier id when none is saved", () => {
    mount(<Catalogue />, {}, false);
    expect(screen.getByText(/no route that finds it/)).toBeTruthy();
  });

  it("lists the products and says so when there are none", async () => {
    mount(<Catalogue />, { get: getter() as never });
    expect(await screen.findByText("Maize meal 12.5kg")).toBeTruthy();
    cleanup();
    mount(<Catalogue />, { get: getter([]) as never });
    expect(await screen.findByText("No products yet. Import a price list to add some.")).toBeTruthy();
  });

  it("edits a product with the price in cents and reloads", async () => {
    const get = getter();
    const patch = vi.fn(async () => ok({ ...product, name: "Maize meal" }));
    mount(<Catalogue />, { get: get as never, patch: patch as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Edit Maize meal 12.5kg" }));

    const price = screen.getByLabelText("Case price (rand)");
    await user.clear(price);
    await user.type(price, "190,50");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Maize meal was updated.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/supplier-products/sp1", expect.objectContaining({ callerPersonId: "p1", casePriceCents: 19050, unitsPerCase: 2 }));
  });

  it("shows the server's refusal when an edit is not allowed", async () => {
    mount(<Catalogue />, { get: getter() as never, patch: vi.fn(async () => fail("permission.denied", "You cannot change this supplier's products.")) as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Edit Maize meal 12.5kg" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect((await screen.findByText("You cannot change this supplier's products.")).getAttribute("role")).toBe("alert");
  });

  it("previews an import before saving it", async () => {
    const result = { applied: false, created: 1, updated: 0, unchanged: 0, errors: 0, lines: [{ row: 2, barcode: null, name: "Rice 10kg", unitsPerCase: 1, casePriceCents: 9000, action: "create", issues: [] }], headers: [], mapping: {} };
    const upload = vi.fn(async () => ok(result));
    mount(<Catalogue upload={upload as never} />, { get: getter() as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Import" }));

    await user.click(screen.getByRole("button", { name: "Preview" }));
    expect(await screen.findByText("Choose a file first.")).toBeTruthy();

    await user.upload(screen.getByLabelText("Price list file"), new File(["a,b"], "prices.csv", { type: "text/csv" }));
    await user.click(screen.getByRole("button", { name: "Preview" }));

    expect(await screen.findByText(/Preview: 1 new/)).toBeTruthy();
    expect(screen.getByText("Rice 10kg")).toBeTruthy();
    const [path, form] = upload.mock.calls[0] as unknown as [string, FormData];
    expect(path).toBe(`/suppliers/${SUPPLIER}/products/import`);
    expect(form.get("apply")).toBe("false");
    expect(form.get("callerPersonId")).toBe("p1");
  });

  it("saves new terms in cents", async () => {
    const put = vi.fn(async () => ok(undefined));
    mount(<Catalogue />, { get: getter() as never, put: put as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Terms" }));
    await waitFor(() => expect((screen.getByLabelText("Minimum order (rand)") as HTMLInputElement).value).toBe("500.00"));

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/terms`, { callerPersonId: "p1", minOrderCents: 50000, deliveryFeeCents: 2500, paymentTerms: "both" });
  });

  it("saves the areas and invites a rep", async () => {
    const put = vi.fn(async () => ok(undefined));
    const post = vi.fn(async () => ok({ membershipId: "m1" }));
    mount(<Catalogue />, { get: getter() as never, put: put as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Areas and days" }));
    await screen.findByDisplayValue("JHB");
    await user.click(screen.getByLabelText("Tue"));
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/areas`, { callerPersonId: "p1", areas: [{ municipalityCode: "JHB", days: ["Mon", "Tue"] }] }));

    await user.click(screen.getByRole("button", { name: "Reps" }));
    await user.type(screen.getByLabelText("Rep name"), "Lerato");
    await user.type(screen.getByLabelText("Rep phone"), "0821234567");
    await user.click(screen.getByRole("button", { name: "Invite rep" }));

    expect(await screen.findByText("Lerato was invited.")).toBeTruthy();
    expect(post).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/reps`, { callerPersonId: "p1", name: "Lerato", phone: "0821234567" });
  });
});
