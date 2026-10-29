// @vitest-environment jsdom
import { fail, ok, queueSale, type SaleUploadDto } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { feature } from "./index";
import { Serials } from "./Serials";

afterEach(cleanup);

const SALE = "0192a3b4-0000-7000-8000-00000000c001";
const sale: SaleUploadDto = {
  id: SALE, staffId: STAFF, soldAt: "2026-10-26T08:00:00Z", paymentMethodCode: "cash", totalCents: 3600, vatCents: 470, tenderedCents: 3600, changeCents: 0, accountId: null,
  lines: [{ productId: "p-bread", name: "Bread", quantity: 2, priceCents: 1800, costCents: 1200, unitDivisor: 1, discountCents: 0, promotionId: null }], payments: null, tipCents: 0, customerId: null,
};

describe("serials feature", () => {
  it("is switched on by the serial_numbers flag", () => {
    expect(feature.flag).toBe("serial_numbers");
    expect(feature.requiresOnline).toBe(true);
  });

  it("records serial numbers against a recent sale's item", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(undefined));
    const { ctx, outbox } = makeCtx({ flags: ["serial_numbers"], api: { post } as never });
    await queueSale(outbox, sale, new Date("2026-10-26T08:00:00Z"));
    render(<Serials ctx={ctx} />);

    await user.selectOptions(await screen.findByRole("combobox", { name: "Sale" }), SALE);
    await user.selectOptions(screen.getByRole("combobox", { name: "Item" }), "p-bread");
    await user.type(screen.getByRole("textbox", { name: "Serial number" }), "SN-001");
    await user.click(screen.getByRole("button", { name: "Add serial number" }));
    await user.type(screen.getByRole("textbox", { name: "Serial number" }), "SN-002{Enter}");
    await user.click(screen.getByRole("button", { name: "Record serial numbers" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/sales/${SALE}/serials`);
    expect(post.mock.calls[0]![1]).toEqual({ deviceId: DEVICE, staffId: STAFF, items: [{ productId: "p-bread", serial: "SN-001" }, { productId: "p-bread", serial: "SN-002" }] });
    expect(await screen.findByText("Recorded 2 serial numbers.")).toBeTruthy();
  });

  it("refuses a repeated serial before asking the server, and a sale reference that is not one", async () => {
    const user = userEvent.setup();
    const post = vi.fn();
    const { ctx } = makeCtx({ flags: ["serial_numbers"], api: { post } as never });
    render(<Serials ctx={ctx} />);

    await user.selectOptions(await screen.findByRole("combobox", { name: "Item" }), "p-bread");
    await user.type(screen.getByRole("textbox", { name: "Serial number" }), "A1");
    await user.click(screen.getByRole("button", { name: "Add serial number" }));
    await user.type(screen.getByRole("textbox", { name: "Serial number" }), "a1");
    await user.click(screen.getByRole("button", { name: "Add serial number" }));
    expect((await screen.findByRole("alert")).textContent).toBe("a1 is already in the list.");

    await user.type(screen.getByRole("textbox", { name: /Or type the sale reference/ }), "not-a-sale");
    await user.click(screen.getByRole("button", { name: "Record serial numbers" }));
    expect((await screen.findByRole("alert")).textContent).toBe("That does not look like a sale reference.");
    expect(post).not.toHaveBeenCalled();
  });

  it("says plainly when the server does not have the sale yet, and shows other refusals", async () => {
    const user = userEvent.setup();
    const post = vi.fn()
      .mockResolvedValueOnce(fail("serial.sale_unknown", "Send the sale to the server first."))
      .mockResolvedValueOnce(fail("serial.already_sold", "Serial SN-1 has already been sold."));
    const { ctx } = makeCtx({ flags: ["serial_numbers"], api: { post } as never });
    render(<Serials ctx={ctx} />);

    await user.type(await screen.findByRole("textbox", { name: /Or type the sale reference/ }), SALE);
    await user.selectOptions(screen.getByRole("combobox", { name: "Item" }), "p-milk");
    await user.type(screen.getByRole("textbox", { name: "Serial number" }), "SN-1{Enter}");
    await user.click(screen.getByRole("button", { name: "Record serial numbers" }));
    expect((await screen.findByRole("alert")).textContent).toContain("does not have that sale yet");

    await user.click(screen.getByRole("button", { name: "Record serial numbers" }));
    await waitFor(() => expect(screen.getAllByRole("alert").some((a) => a.textContent === "Serial SN-1 has already been sold.")).toBe(true));
  });

  it("searches for a serial number and shows the empty result", async () => {
    const user = userEvent.setup();
    const get = vi.fn()
      .mockResolvedValueOnce(ok([{ productId: "p1", productName: "Kettle", serial: "KX-9001", saleId: SALE, soldAt: "2026-10-26T08:00:00Z" }]))
      .mockResolvedValueOnce(ok([]));
    const { ctx } = makeCtx({ flags: ["serial_numbers"], api: { get } as never });
    render(<Serials ctx={ctx} />);

    await user.click(screen.getByRole("button", { name: "Find a serial number" }));
    await user.type(screen.getByRole("textbox", { name: /Serial number or part of it/ }), "KX-9");
    await user.click(screen.getByRole("button", { name: "Find" }));
    expect(await screen.findByText("Kettle")).toBeTruthy();
    expect(get).toHaveBeenCalledWith(`/shops/${SHOP}/serials`, { text: "KX-9", deviceId: DEVICE, staffId: STAFF });

    await user.click(screen.getByRole("button", { name: "Find" }));
    expect(await screen.findByText("No sold item has that serial number.")).toBeTruthy();
  });
});
