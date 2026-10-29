// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, SUPPLIER } from "../../shared/testing";
import { Application } from "./Application";

afterEach(cleanup);

const supplier = { id: SUPPLIER, name: "Cash and Carry", status: "verified", rejectionReason: null, minOrderCents: 50000, deliveryFeeCents: 2500, paymentTerms: "both", areas: [{ municipalityCode: "JHB", days: ["Mon", "Thu"] }], verified: true };

async function fillAndSend(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Business name"), "Cash and Carry");
  await user.type(screen.getByLabelText("Contact person"), "Sipho");
  await user.type(screen.getByLabelText("Municipality code"), "JHB");
  await user.click(screen.getByLabelText("Mon"));
  await user.click(screen.getByRole("button", { name: "Send application" }));
}

describe("Application", () => {
  it("shows the saved supplier's details", async () => {
    const get = vi.fn(async () => ok(supplier));
    mount(<Application />, { get: get as never });

    expect(await screen.findByText("Cash and Carry")).toBeTruthy();
    expect(get).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}`, { callerPersonId: "p1" });
    expect(screen.getByText("JHB: Mon, Thu")).toBeTruthy();
  });

  it("says no supplier is chosen when none is saved", () => {
    mount(<Application />, {}, false);
    expect(screen.getByText(/No supplier chosen yet/)).toBeTruthy();
  });

  it("sends the application and remembers the supplier id", async () => {
    const post = vi.fn(async () => ok({ supplierId: SUPPLIER, organisationId: "o9", status: "pending" }));
    mount(<Application />, { get: vi.fn(async () => ok(supplier)) as never, post: post as never }, false);
    const user = userEvent.setup({ delay: null });

    await fillAndSend(user);

    await waitFor(() => expect(screen.getByText(/Application sent. Status: pending/)).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/suppliers/applications", expect.objectContaining({
      applicantPersonId: "p1", name: "Cash and Carry", contactName: "Sipho", registrationNo: null, areas: [{ municipalityCode: "JHB", days: ["Mon"] }],
    }));
    expect(localStorage.getItem("tillknowit.supplierId")).toBe(SUPPLIER);
  });

  it("shows the server's refusal", async () => {
    mount(<Application />, { get: vi.fn(async () => ok(supplier)) as never, post: vi.fn(async () => fail("supplier.name_taken", "A supplier with that name already exists.")) as never });
    const user = userEvent.setup({ delay: null });

    await fillAndSend(user);

    expect((await screen.findByText("A supplier with that name already exists.")).getAttribute("role")).toBe("alert");
  });
});
