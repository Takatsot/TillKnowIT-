// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { Customers } from "./Customers";
import { feature } from "./index";

afterEach(cleanup);

const anele = { id: "c1", name: "Anele Dlamini", phone: "0821234567", email: null, messageConsent: true, consentAt: "2026-09-01T08:00:00Z", points: 120 };
const points = { customerId: "c1", points: 120, valueCents: 1200, minRedeemPoints: 50, recent: [{ type: "earn", points: 20, note: null, at: "2026-10-01T08:00:00Z", saleId: null }] };

describe("customers feature", () => {
  it("is switched on by the customers flag", () => {
    expect(feature.flag).toBe("customers");
    expect(feature.requiresOnline).toBe(true);
  });

  it("finds a customer by phone and shows points only when loyalty is on", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async () => ok([anele]));
    const { ctx } = makeCtx({ flags: ["customers", "loyalty"], api: { get } as never });
    render(<Customers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "082123");
    await user.click(screen.getByRole("button", { name: "Find" }));

    expect(await screen.findByText("Anele Dlamini")).toBeTruthy();
    expect(screen.getByText("120")).toBeTruthy();
    expect(get).toHaveBeenCalledWith(`/shops/${SHOP}/customers`, { search: "082123", deviceId: DEVICE, staffId: STAFF });
  });

  it("does not search for fewer than 3 characters and shows the empty result", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async () => ok([]));
    const { ctx } = makeCtx({ flags: ["customers"], api: { get } as never });
    render(<Customers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "08");
    await user.click(screen.getByRole("button", { name: "Find" }));
    expect(await screen.findByText("Type at least 3 characters.", { selector: "span" })).toBeTruthy();
    expect(get).not.toHaveBeenCalled();

    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "2");
    await user.click(screen.getByRole("button", { name: "Find" }));
    expect(await screen.findByText("No customer found. You can add them below.")).toBeTruthy();
  });

  it("adds a customer with consent off unless the box is ticked, using the consent wording", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, body?: unknown) => ok({ ...anele, ...(body as object), points: 0 }));
    const { ctx } = makeCtx({ flags: ["customers"], api: { get: vi.fn(async () => ok([])), post } as never });
    render(<Customers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Add a customer" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Thabo");
    await user.type(screen.getByRole("textbox", { name: "Cell phone number" }), "0829998888");
    const consent = screen.getByRole("checkbox", { name: /agrees that this shop may send them messages/ }) as HTMLInputElement;
    expect(consent.checked).toBe(false);
    await user.click(screen.getByRole("button", { name: "Save customer" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF, name: "Thabo", phone: "0829998888", messageConsent: false, consentSource: null });
    expect(await screen.findByText("Thabo was saved.")).toBeTruthy();
  });

  it("records consent with its source when the customer agreed", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, body?: unknown) => ok({ ...anele, ...(body as object), points: 0 }));
    const { ctx } = makeCtx({ flags: ["customers"], api: { post } as never });
    render(<Customers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Add a customer" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Thabo");
    await user.type(screen.getByRole("textbox", { name: "Cell phone number" }), "0829998888");
    await user.click(screen.getByRole("checkbox", { name: /agrees that this shop may send them messages/ }));
    await user.click(screen.getByRole("button", { name: "Save customer" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![1]).toMatchObject({ messageConsent: true, consentSource: "till" });
  });

  it("shows the server's refusal for a bad phone number", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail("customer.phone_invalid", "That is not a South African phone number."));
    const { ctx } = makeCtx({ flags: ["customers"], api: { post } as never });
    render(<Customers ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Add a customer" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Thabo");
    await user.type(screen.getByRole("textbox", { name: "Cell phone number" }), "123");
    await user.click(screen.getByRole("button", { name: "Save customer" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That is not a South African phone number.");
  });

  it("shows the points balance and redeems points, giving the payment reference", async () => {
    const user = userEvent.setup();
    const get = vi.fn(async (path: string) => ok(path.endsWith("/points") ? points : [anele]));
    const post = vi.fn(async (_p: string, _b?: unknown) => ok({ redemptionReference: "points:abc", points: 100, valueCents: 1000, balanceAfter: 20 }));
    const { ctx } = makeCtx({ flags: ["customers", "loyalty"], api: { get, post } as never });
    render(<Customers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "Anele");
    await user.click(screen.getByRole("button", { name: "Find" }));
    await user.click(await screen.findByRole("button", { name: "Open Anele Dlamini" }));
    expect(await screen.findByText(/120 points, worth R\s?12,00/)).toBeTruthy();

    await user.type(screen.getByRole("textbox", { name: "Points to use" }), "500");
    await user.click(screen.getByRole("button", { name: "Use points" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The customer has only 120 points.");

    await user.clear(screen.getByRole("textbox", { name: "Points to use" }));
    await user.type(screen.getByRole("textbox", { name: "Points to use" }), "100");
    await user.click(screen.getByRole("button", { name: "Use points" }));

    expect(await screen.findByText("Payment reference for the sale: points:abc")).toBeTruthy();
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/customers/c1/points/redeem`);
    expect(post.mock.calls[0]![1]).toMatchObject({ points: 100 });
  });

  it("hides loyalty when that flag is off", async () => {
    const user = userEvent.setup();
    const { ctx } = makeCtx({ flags: ["customers"], api: { get: vi.fn(async () => ok([anele])) } as never });
    render(<Customers ctx={ctx} />);

    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "Anele");
    await user.click(screen.getByRole("button", { name: "Find" }));
    await user.click(await screen.findByRole("button", { name: "Open Anele Dlamini" }));

    expect(screen.queryByText("Loyalty points")).toBeNull();
  });
});

describe("customer to the sale on the till", () => {
  it("attaches the customer to the sale being rung up and shows the Sell tab", async () => {
    const user = userEvent.setup();
    const setCustomer = vi.fn();
    const openSell = vi.fn();
    const { ctx } = makeCtx({ flags: ["customers"], api: { get: vi.fn(async () => ok([anele])) as never }, sale: { addLine: vi.fn(), addProduct: vi.fn(), setCustomer }, openSell });
    render(<Customers ctx={ctx} />);
    await user.type(screen.getByRole("textbox", { name: /Find a customer/ }), "082123");
    await user.click(screen.getByRole("button", { name: "Find" }));
    await user.click(await screen.findByRole("button", { name: "Open Anele Dlamini" }));

    await user.click(screen.getByRole("button", { name: "Add Anele Dlamini to the sale on the till" }));

    expect(setCustomer).toHaveBeenCalledWith("c1", "Anele Dlamini");
    expect(openSell).toHaveBeenCalled();
  });
});
