// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, SUPPLIER } from "../../shared/testing";
import { DealsArea } from "./DealsArea";

afterEach(cleanup);

const deal = { id: "d1", title: "Maize special", type: "discount", startsAt: "2026-11-01T08:00:00Z", endsAt: "2026-11-08T08:00:00Z", areas: ["JHB"], isSponsored: true, label: "Sponsored", isActive: true };
const group = { id: "g1", supplierProductId: "sp1", productName: "Maize meal", bulkPriceCents: 16000, minTotalCases: 20, totalCases: 8, progressPercent: 40, closesAt: "2026-11-10T08:00:00Z", areaCode: "JHB", status: "open", shops: 3, buyingGroupId: null };
const products = [{ id: "sp1", name: "Maize meal" }, { id: "sp2", name: "Rice" }];

function getter(deals: unknown[] = [deal], groups: unknown[] = [group]) {
  return vi.fn(async (path: string) => ok(path.endsWith("/products") ? products : path.endsWith("/group-deals") ? groups : deals));
}

describe("Deals", () => {
  it("lists deals with the Sponsored label, and says so when there are none", async () => {
    mount(<DealsArea />, { get: getter() as never });
    expect(await screen.findByText("Maize special")).toBeTruthy();
    expect(screen.getByText("Sponsored")).toBeTruthy();
    cleanup();
    mount(<DealsArea />, { get: getter([]) as never });
    expect(await screen.findByText("No deals yet.")).toBeTruthy();
  });

  it("posts a percentage discount for the chosen product", async () => {
    const post = vi.fn(async () => ok({ ...deal, title: "Rice week" }));
    mount(<DealsArea />, { get: getter() as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Maize special");
    await screen.findByRole("option", { name: "Rice" });

    await user.type(screen.getByLabelText("Title"), "Rice week");
    await user.selectOptions(screen.getByLabelText("Product"), "sp2");
    await user.type(screen.getByLabelText("Percent off (1 to 90)"), "10");
    await user.type(screen.getByLabelText("Starts"), "2026-11-01T08:00");
    await user.type(screen.getByLabelText("Ends"), "2026-11-08T08:00");
    await user.type(screen.getByLabelText("Areas (municipality codes, separated by commas)"), "JHB, PTA");
    await user.click(screen.getByRole("button", { name: "Post deal" }));

    await waitFor(() => expect(screen.getByText("Rice week was posted.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/deals`, expect.objectContaining({
      callerPersonId: "p1", title: "Rice week", type: "discount", areas: ["JHB", "PTA"], isSponsored: false,
      terms: expect.objectContaining({ supplierProductId: "sp2", percentOff: 10, centsOffPerCase: null }),
    }));
  });

  it("explains sponsored deals and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("deal.sponsorship_not_paid", "Sponsored deals need a sponsored campaign with budget left on your plan."));
    mount(<DealsArea />, { get: getter() as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("Maize special");

    await user.type(screen.getByLabelText("Title"), "Rice week");
    await user.type(screen.getByLabelText("Starts"), "2026-11-01T08:00");
    await user.type(screen.getByLabelText("Ends"), "2026-11-08T08:00");
    await user.type(screen.getByLabelText("Areas (municipality codes, separated by commas)"), "JHB");
    await user.click(screen.getByLabelText("Make this a sponsored deal"));
    expect(screen.getByText(/always labelled Sponsored/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Post deal" }));

    expect((await screen.findByText(/need a sponsored campaign with budget left/)).getAttribute("role")).toBe("alert");
  });

  it("cancels a deal", async () => {
    const patch = vi.fn(async () => ok(undefined));
    const get = getter();
    mount(<DealsArea />, { get: get as never, patch: patch as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Cancel Maize special" }));

    await waitFor(() => expect(screen.getByText("Maize special was cancelled.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/deals/d1/cancel", { callerPersonId: "p1" });
  });

  it("lists group deals and closes one early", async () => {
    const post = vi.fn(async () => ok(undefined));
    mount(<DealsArea />, { get: getter() as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Group deals" }));

    expect(await screen.findByText("8 of 20 cases (40%), 3 shops")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Close Maize meal early" }));

    await waitFor(() => expect(screen.getByText("Maize meal was closed.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/group-deals/g1/close", undefined, { callerPersonId: "p1" });
  });

  it("posts an invite-only group deal and says when there are none", async () => {
    const post = vi.fn(async () => ok({ ...group, productName: "Rice" }));
    mount(<DealsArea />, { get: getter([], []) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Group deals" }));
    expect(await screen.findByText("No group deals yet.")).toBeTruthy();
    await screen.findByRole("option", { name: "Rice" });

    await user.selectOptions(screen.getByLabelText("Product"), "sp2");
    await user.type(screen.getByLabelText("Bulk price per case (rand)"), "150");
    await user.type(screen.getByLabelText("Minimum total cases"), "30");
    await user.type(screen.getByLabelText("Closes"), "2026-11-10T08:00");
    await user.type(screen.getByLabelText("Area code"), "JHB");
    await user.type(screen.getByLabelText("Buying group id (optional)"), "bg1");
    await user.click(screen.getByRole("button", { name: "Post group deal" }));

    await waitFor(() => expect(screen.getByText("The group deal for Rice was posted.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith(`/suppliers/${SUPPLIER}/group-deals`, expect.objectContaining({
      supplierProductId: "sp2", bulkPriceCents: 15000, minTotalCases: 30, areaCode: "JHB", buyingGroupId: "bg1",
    }));
  });
});
