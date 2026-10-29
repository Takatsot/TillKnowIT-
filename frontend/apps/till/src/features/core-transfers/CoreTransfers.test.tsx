// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fail, ok } from "@tillknowit/domain";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCoreCtx, SHOP, STAFF } from "../../coreTesting";
import { CoreTransfers, type Transfer } from "./CoreTransfers";
import { feature } from "./index";

afterEach(cleanup);

const OTHER = "0192a3b4-0000-7000-8000-00000000b002";
const incoming: Transfer = { id: "t-in", fromShopId: OTHER, toShopId: SHOP, status: "sent", sentAt: "2026-10-26T08:00:00.000Z", closedAt: null, note: "Weekly top-up", lines: [{ productId: "p-bread", name: "Bread", quantity: 10, receivedQuantity: null, shortfall: 0 }, { productId: "p-milk", name: "Milk 1L", quantity: 6, receivedQuantity: null, shortfall: 0 }] };
const outgoing: Transfer = { id: "t-out", fromShopId: SHOP, toShopId: OTHER, status: "sent", sentAt: "2026-10-26T09:00:00.000Z", closedAt: null, note: null, lines: [{ productId: "p-bread", name: "Bread", quantity: 3, receivedQuantity: null, shortfall: 0 }] };
const done: Transfer = { ...outgoing, id: "t-done", status: "received", lines: [{ productId: "p-bread", name: "Bread", quantity: 5, receivedQuantity: 4, shortfall: 1 }] };

function api(over: { list?: Transfer[]; post?: ReturnType<typeof vi.fn> } = {}) {
  const post = over.post ?? vi.fn(async () => ok({}));
  const get = vi.fn(async () => ok(over.list ?? [incoming, outgoing, done]));
  return { get, post } as never as { get: typeof get; post: typeof post };
}

const sendButton = () => screen.getAllByRole("button", { name: "Send stock" }).at(-1)!;

describe("Transfers tab", () => {
  it("is for the owner and the manager and is greyed out offline", () => {
    expect(feature.roles).toEqual(["owner", "manager"]);
    expect(feature.requiresOnline).toBe(true);
  });

  it("tells the person to connect when the till is offline", () => {
    const { ctx } = makeCoreCtx({ api: api(), online: false });
    render(<CoreTransfers ctx={ctx} />);

    expect(screen.getByRole("status").textContent).toContain("need the internet");
  });

  it("lists what is coming in and what was sent, asking the server with this till and the cashier named", async () => {
    const a = api();
    const { ctx } = makeCoreCtx({ api: a });
    const user = userEvent.setup();
    render(<CoreTransfers ctx={ctx} />);

    expect(await screen.findByText(`From shop ${OTHER}`)).toBeTruthy();
    expect(screen.getByText("Note: Weekly top-up")).toBeTruthy();
    expect(a.get).toHaveBeenCalledWith(`/shops/${SHOP}/stock-transfers`, { deviceId: DEVICE, staffId: STAFF });

    await user.click(screen.getByRole("button", { name: "Sent" }));
    expect(screen.getByText("Bread: 4 of 5 received (short by 1)")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Cancel transfer" })).toBeTruthy();
  });

  it("receives everything with no lines when all of it came, and says what arrived when something is short", async () => {
    const post = vi.fn(async () => ok({}));
    const { ctx } = makeCoreCtx({ api: api({ post }) });
    const user = userEvent.setup();
    render(<CoreTransfers ctx={ctx} />);
    await screen.findByText(`From shop ${OTHER}`);

    await user.click(screen.getByRole("button", { name: "Receive" }));
    await waitFor(() => expect(screen.getByText("Transfer received.")).toBeTruthy());
    expect(post).toHaveBeenLastCalledWith(`/shops/${SHOP}/stock-transfers/t-in/receive`, { deviceId: DEVICE, staffId: STAFF, lines: undefined });

    const milk = screen.getByRole("textbox", { name: "Received quantity of Milk 1L" });
    await user.clear(milk);
    await user.type(milk, "4");
    await user.click(screen.getByRole("button", { name: "Receive" }));
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
    expect(post).toHaveBeenLastCalledWith(`/shops/${SHOP}/stock-transfers/t-in/receive`, { deviceId: DEVICE, staffId: STAFF, lines: [{ productId: "p-bread", quantity: 10 }, { productId: "p-milk", quantity: 4 }] });
  });

  it("cancels a transfer this shop sent", async () => {
    const post = vi.fn(async () => ok({}));
    const { ctx } = makeCoreCtx({ api: api({ post }) });
    const user = userEvent.setup();
    render(<CoreTransfers ctx={ctx} />);
    await user.click(await screen.findByRole("button", { name: "Sent" }));

    await user.click(screen.getByRole("button", { name: "Cancel transfer" }));

    await waitFor(() => expect(screen.getByText("Transfer cancelled.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/stock-transfers/t-out/cancel`, { deviceId: DEVICE, staffId: STAFF });
  });

  it("sends stock with an id made on the till, and keeps the same id when the first try fails", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce(fail("network.unreachable", "We cannot reach the server."))
      .mockResolvedValueOnce(ok({}));
    const { ctx } = makeCoreCtx({ api: api({ post }) });
    const user = userEvent.setup();
    render(<CoreTransfers ctx={ctx} />);
    await screen.findByText(`From shop ${OTHER}`);
    await user.click(screen.getByRole("button", { name: "Send stock" }));
    await user.type(screen.getByRole("textbox", { name: "Send to shop (number from the back office)" }), OTHER);
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");
    const quantity = screen.getByRole("textbox", { name: "Quantity of Bread" });
    await user.clear(quantity);
    await user.type(quantity, "8");
    await user.type(screen.getByRole("textbox", { name: "Note (optional)" }), "For Friday");

    await user.click(sendButton());
    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
    await user.click(sendButton());

    await waitFor(() => expect(screen.getByText(/Stock sent\./)).toBeTruthy());
    const [first, second] = post.mock.calls as unknown as Array<[string, { id: string; toShopId: string; lines: unknown; note: string }]>;
    expect(first![0]).toBe(`/shops/${SHOP}/stock-transfers`);
    expect(first![1]).toMatchObject({ toShopId: OTHER, lines: [{ productId: "p-bread", quantity: 8, toProductId: null }], note: "For Friday" });
    expect(second![1].id).toBe(first![1].id);
  });

  it("checks the shop number and the quantities before asking the server", async () => {
    const post = vi.fn(async () => ok({}));
    const { ctx } = makeCoreCtx({ api: api({ post }) });
    const user = userEvent.setup();
    render(<CoreTransfers ctx={ctx} />);
    await screen.findByText(`From shop ${OTHER}`);
    await user.click(screen.getByRole("button", { name: "Send stock" }));
    await user.type(screen.getByRole("textbox", { name: "Scan a barcode or type a product name" }), "6001000000011{Enter}");

    await user.type(screen.getByRole("textbox", { name: "Send to shop (number from the back office)" }), "nonsense");
    await user.click(sendButton());
    expect(screen.getByRole("alert").textContent).toContain("shop number does not look right");

    await user.clear(screen.getByRole("textbox", { name: "Send to shop (number from the back office)" }));
    await user.type(screen.getByRole("textbox", { name: "Send to shop (number from the back office)" }), SHOP);
    await user.click(sendButton());
    expect(screen.getByRole("alert").textContent).toContain("That is this shop");
    expect(post).not.toHaveBeenCalled();
  });

  it("explains a plan that does not include transfers instead of showing the code", async () => {
    const a = { get: vi.fn(async () => fail("plan.feature_not_included", "raw server words")), post: vi.fn() } as never;
    const { ctx } = makeCoreCtx({ api: a });
    render(<CoreTransfers ctx={ctx} />);

    expect((await screen.findByRole("alert")).textContent).toContain("Your plan does not include moving stock between branches");
  });

  it("says so when nothing is coming in", async () => {
    const { ctx } = makeCoreCtx({ api: api({ list: [] }) });
    render(<CoreTransfers ctx={ctx} />);

    expect(await screen.findByText("Nothing is on its way to this shop.")).toBeTruthy();
  });
});
