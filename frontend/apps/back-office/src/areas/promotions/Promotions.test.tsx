// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Promotions, type Promotion } from "./Promotions";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const stock = [{ productId: "milk", name: "Milk" }, { productId: "bread", name: "Bread" }];
const promo: Promotion = {
  id: "pr1", name: "Friday milk", type: "percent_off", productIds: ["milk"], percent: 10, amountCents: 0, items: [],
  startsAt: "2026-11-01T00:00:00+02:00", endsAt: "2026-11-30T23:59:59+02:00", days: ["fri"], from: "16:00", to: "18:00", isActive: false,
};

function routes(extra: Record<string, unknown> = {}, list: Promotion[] = [promo]) {
  return vi.fn(async (path: string) => {
    if (path.endsWith("/stock")) return ok(stock);
    if (path.endsWith("/promotions")) return (extra.promotions as never) ?? ok(list);
    return fail("x", "unexpected " + path);
  });
}

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Promotions /></SessionProvider>);
}

describe("Promotions", () => {
  it("lists promotions with their deal and when, and says when there are none", async () => {
    mount({ get: routes() as never });
    expect(await screen.findByText("Friday milk")).toBeTruthy();
    expect(screen.getByText("10% off")).toBeTruthy();
    expect(screen.getByText(/Days: Fri/)).toBeTruthy();
    cleanup();
    mount({ get: routes({}, []) as never });
    expect(await screen.findByText("No promotions yet.")).toBeTruthy();
  });

  it("turns a promotion on with the caller person", async () => {
    const post = vi.fn(async () => ok({ ...promo, isActive: true }));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Turn on Friday milk" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/promotions/pr1/active", { callerPersonId: "p1", active: true }));
    expect(await screen.findByText("Friday milk is now On.")).toBeTruthy();
  });

  it("creates an amount-off promotion with the rand turned into cents", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok({ ...promo, name: "Bread deal", type: "amount_off" }));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Friday milk");

    await user.type(screen.getByLabelText("Name"), "Bread deal");
    await user.selectOptions(screen.getByLabelText("Kind of promotion"), "amount_off");
    await user.type(screen.getByLabelText("Amount in rand"), "2,50");
    fireEvent.change(screen.getByLabelText("First day"), { target: { value: "2026-11-01" } });
    fireEvent.change(screen.getByLabelText("Last day"), { target: { value: "2026-11-30" } });
    await user.click(await screen.findByLabelText("Bread"));
    await user.click(screen.getByRole("button", { name: "Save promotion" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    const [path, body] = post.mock.calls[0] as unknown as [string, Record<string, unknown>];
    expect(path).toBe("/shops/s1/promotions");
    expect(body).toMatchObject({ callerPersonId: "p1", name: "Bread deal", type: "amount_off", amountCents: 250, productIds: ["bread"], days: null, startsAt: "2026-11-01T00:00:00+02:00" });
    expect(await screen.findByText("Bread deal was saved.")).toBeTruthy();
  });

  it("edits a promotion with PATCH and shows the server's refusal", async () => {
    const patch = vi.fn(async (..._a: unknown[]) => fail("promotion.dates_invalid", "The last day is before the first day."));
    mount({ get: routes() as never, patch: patch as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Edit Friday milk" }));
    expect(screen.getByRole("heading", { name: "Edit promotion" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Save promotion" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The last day is before the first day.");
    expect(patch.mock.calls[0]![0]).toBe("/shops/s1/promotions/pr1");
  });

  it("prices a pretend basket", async () => {
    const post = vi.fn(async () => ok({ subtotalCents: 2000, discountCents: 200, totalCents: 1800, discountsByProduct: {}, applied: [{ promotionId: "pr1", name: "Friday milk", discountCents: 200, times: 1 }] }));
    mount({ get: routes() as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Friday milk");
    await user.selectOptions(screen.getByLabelText("Product 1"), "milk");
    await user.type(screen.getByLabelText("Unit price in rand 1"), "20");
    await user.click(screen.getByRole("button", { name: "Work out the price" }));

    expect(await screen.findByText(/Friday milk: .*2,00 off, 1 time/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/shops/s1/price-basket", { deviceId: "d1", staffId: "p1", lines: [{ productId: "milk", quantity: 1, unitPriceCents: 2000 }], at: null });
  });
});
