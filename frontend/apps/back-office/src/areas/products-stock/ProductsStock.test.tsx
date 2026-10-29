// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProductsStock } from "./ProductsStock";

afterEach(cleanup);
vi.setConfig({ testTimeout: 30000 });

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const row = { productId: "pr1", name: "Maize meal 5kg", barcode: "600123", onHand: 2, lowStockLevel: 5, expiryDate: null, daysLeft: 3, suggestedOrderQuantity: 12, flags: ["low"] };
const categories = [{ code: "grocery", names: { en: "Grocery" }, isActive: true }];

function getter(rows: unknown[]) {
  return vi.fn(async (path: string) => (path === "/lookups/categories" ? ok(categories) : ok(rows)));
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><ProductsStock /></SessionProvider>);
}

describe("ProductsStock", () => {
  it("lists stock with the flag and the suggested order, and filters through the server", async () => {
    const get = getter([row]);
    mount({ get: get as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Maize meal 5kg")).toBeTruthy();
    expect(screen.getByText("12 units")).toBeTruthy();
    expect(screen.getByText("Low")).toBeTruthy();

    await user.selectOptions(screen.getByLabelText("Show"), "out");
    await waitFor(() => expect(get).toHaveBeenCalledWith("/shops/s1/stock", { filter: "out" }));
  });

  it("shows the empty state", async () => {
    mount({ get: getter([]) as never });
    expect(await screen.findByText("No products match this view.")).toBeTruthy();
  });

  it("validates, then creates a product with the device and person as stand-ins", async () => {
    const post = vi.fn(async () => ok({ id: "x", status: "accepted" }));
    mount({ get: getter([row]) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Maize meal 5kg");

    await user.click(screen.getByRole("button", { name: "Save product" }));
    expect(screen.getByText("Enter the product name.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Product name"), "Sugar 2kg");
    await user.selectOptions(screen.getByLabelText("Category"), "grocery");
    await user.type(screen.getByLabelText("Cost price (R)"), "30");
    await user.type(screen.getByLabelText("Selling price (R)"), "39,99");
    await user.click(screen.getByRole("button", { name: "Save product" }));

    await waitFor(() => expect(screen.getByText("Sugar 2kg was added.")).toBeTruthy());
    const [path, body] = post.mock.calls[0] as unknown as [string, { shopId: string; deviceId: string; product: Record<string, unknown> }];
    expect(path).toBe("/products");
    expect(body.deviceId).toBe("d1");
    expect(body.product).toMatchObject({ staffId: "p1", name: "Sugar 2kg", categoryCode: "grocery", costCents: 3000, priceCents: 3999, isActive: true });
  });

  it("edits through PATCH and shows the server's refusal", async () => {
    const patch = vi.fn(async () => fail("product.duplicate_barcode", "Another product already has this barcode."));
    mount({ get: getter([row]) as never, patch: patch as never });
    const user = userEvent.setup();
    await screen.findByText("Maize meal 5kg");

    await user.click(screen.getByRole("button", { name: "Edit Maize meal 5kg" }));
    await user.selectOptions(screen.getByLabelText("Category"), "grocery");
    await user.type(screen.getByLabelText("Cost price (R)"), "50");
    await user.type(screen.getByLabelText("Selling price (R)"), "65");
    await user.click(screen.getByRole("button", { name: "Save product" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Another product already has this barcode.");
    expect((patch.mock.calls[0] as unknown as [string])[0]).toBe("/products/pr1");
  });

  it("shows the server's message when stock cannot be loaded", async () => {
    mount({ get: vi.fn(async (p: string) => (p === "/lookups/categories" ? ok([]) : fail("network.unreachable", "We cannot reach the server."))) as never });
    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
  });
});
