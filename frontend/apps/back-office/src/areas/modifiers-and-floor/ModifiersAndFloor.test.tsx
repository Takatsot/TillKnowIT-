// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ModifiersAndFloor } from "./ModifiersAndFloor";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const stock = [{ productId: "milk", name: "Milk" }, { productId: "ketchup", name: "Ketchup" }];
const group = { id: "g1", name: "Sauces", required: false, min: 0, max: 2, options: [{ productId: "ketchup", name: "Ketchup", priceCents: 500 }] };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><ModifiersAndFloor /></SessionProvider>);
}

type Handler = (path: string, query?: unknown) => unknown;
const get = (h: Handler) => vi.fn(async (path: string, query?: unknown) => (path.endsWith("/stock") ? ok(stock) : h(path, query))) as never;

describe("Modifiers and floor", () => {
  it("lists modifier groups, and says when there are none", async () => {
    mount({ get: get(() => ok([group])) });
    expect(await screen.findByText("Sauces")).toBeTruthy();
    expect(screen.getByText(/Ketchup \(.*5,00\)/)).toBeTruthy();
    cleanup();
    mount({ get: get(() => ok([])) });
    expect(await screen.findByText("No modifier groups yet.")).toBeTruthy();
  });

  it("creates a group with the chosen extras", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok(group));
    mount({ get: get(() => ok([])), post: post as never });
    const user = userEvent.setup();
    await screen.findByText("No modifier groups yet.");

    await user.type(screen.getByLabelText("Group name"), "Sauces");
    await user.click(await screen.findByLabelText("Ketchup"));
    await user.click(screen.getByRole("button", { name: "Save group" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/modifier-groups", { callerPersonId: "p1", name: "Sauces", required: false, min: 0, max: 1, optionProductIds: ["ketchup"] }));
    expect(await screen.findByText("Sauces was saved.")).toBeTruthy();
  });

  it("puts groups on a product", async () => {
    const put = vi.fn(async (..._a: unknown[]) => ok(undefined));
    mount({ get: get((path) => (path.includes("/products/milk/") ? ok([]) : ok([group]))), put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Sauces");
    await user.selectOptions(screen.getByLabelText("Product"), "milk");
    await user.click(await screen.findByLabelText("Sauces"));
    await user.click(screen.getByRole("button", { name: "Save groups for this product" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith("/shops/s1/products/milk/modifier-groups", { callerPersonId: "p1", groupIds: ["g1"] }));
    expect(await screen.findByText("The groups for this product were saved.")).toBeTruthy();
  });

  it("shows the server's message when a group cannot be saved", async () => {
    mount({ get: get(() => ok([])), post: vi.fn(async () => fail("modifier.min_above_max", "The fewest cannot be more than the most.")) as never });
    const user = userEvent.setup();
    await screen.findByText("No modifier groups yet.");
    await user.type(screen.getByLabelText("Group name"), "Sauces");
    await user.click(screen.getByRole("button", { name: "Save group" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The fewest cannot be more than the most.");
  });

  it("adds a variant with prices in cents", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok([]));
    mount({ get: get((path) => (path.endsWith("/variants") ? ok([]) : ok([]))), post: post as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Variants" }));
    await user.selectOptions(await screen.findByLabelText("Product"), "milk");
    expect(await screen.findByText("This product has no variants yet.")).toBeTruthy();

    await user.type(screen.getByLabelText("Variant label (for example Large)"), "2 litre");
    await user.type(screen.getByLabelText("Cost in rand"), "20");
    await user.type(screen.getByLabelText("Price in rand"), "29,99");
    await user.click(screen.getByRole("button", { name: "Add variant" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/products/milk/variants", { callerPersonId: "p1", variants: [{ label: "2 litre", barcode: null, costCents: 2000, priceCents: 2999 }] }));
  });

  it("saves the whole floor plan", async () => {
    const put = vi.fn(async (..._a: unknown[]) => ok([{ id: "t1", area: "Patio", name: "T1", seats: 4 }]));
    mount({ get: get(() => ok([])), put: put as never });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Floor plan" }));
    expect(await screen.findByText("No tables yet.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Add table" }));
    await user.type(screen.getByLabelText("Area 1"), "Patio");
    await user.type(screen.getByLabelText("Table 1"), "T1");
    await user.click(screen.getByRole("button", { name: "Save floor plan" }));

    await waitFor(() => expect(put).toHaveBeenCalledWith("/shops/s1/floor-plan", { callerPersonId: "p1", tables: [{ area: "Patio", name: "T1", seats: 4 }] }));
    expect(await screen.findByText("The floor plan was saved (1 tables).")).toBeTruthy();
  });
});
