// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SessionProvider } from "@tillknowit/app-kit";
import type { Api } from "@tillknowit/api-client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SupplierGate } from "./SupplierGate";
import { session, SUPPLIER } from "./testing";

afterEach(cleanup);

function gate(get: unknown) {
  try { localStorage.clear(); } catch { /* ignore */ }
  return render(<SessionProvider api={{ get } as unknown as Api} initial={session}><SupplierGate>{(id) => <p>Screen for {id}</p>}</SupplierGate></SessionProvider>);
}

describe("SupplierGate", () => {
  it("finds the supplier from the organisation without typing", async () => {
    const get = vi.fn(async () => ok({ supplierId: SUPPLIER, organisationId: "org1", name: "Cash and Carry", status: "verified" }));
    gate(get);
    expect(await screen.findByText(`Screen for ${SUPPLIER}`)).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/organisations/org1/supplier", { callerPersonId: "p1" });
  });

  it("offers a typed id only when the server has no supplier yet", async () => {
    gate(vi.fn(async () => fail("supplier.not_found", "This business has no supplier application yet.")));
    const user = userEvent.setup({ delay: null });
    await user.type(await screen.findByLabelText("Supplier id"), SUPPLIER);
    await user.click(screen.getByRole("button", { name: "Use this supplier" }));
    expect(await screen.findByText(`Screen for ${SUPPLIER}`)).toBeTruthy();
  });

  it("shows another refusal and no id box", async () => {
    gate(vi.fn(async () => fail("permission.denied", "You do not work for this supplier.")));
    expect((await screen.findByText("You do not work for this supplier.")).getAttribute("role")).toBe("alert");
    expect(screen.queryByLabelText("Supplier id")).toBeNull();
  });
});
