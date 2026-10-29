// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, thandi } from "../../shared/testing";
import { Requests } from "./Requests";

afterEach(cleanup);

const children = { get: vi.fn(async () => ok([thandi])) as never };

describe("Requests", () => {
  it("asks for a refund and shows the amount and status", async () => {
    const post = vi.fn(async () => ok({ id: "r1", walletId: "w1", amountCents: 5000, status: "Requested" }));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Ask for a refund" }));

    await waitFor(() => expect(screen.getByText(rand("Your request was sent. Amount: R50,00"), { exact: false })).toBeTruthy());
    expect(screen.getByText("waiting for the school", { exact: false })).toBeTruthy();
    expect(post).toHaveBeenCalledWith("/wallets/w1/refund-requests", { callerPersonId: "p1" });
  });

  it("shows the server's message when the child is still at the school", async () => {
    const post = vi.fn(async () => fail("refund.child_still_enrolled", "A refund is for when your child leaves the school. Ask the school to mark them as left."));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Ask for a refund" }));

    expect((await screen.findByRole("alert")).textContent).toContain("A refund is for when your child leaves the school.");
  });

  it("marks a child as left only after confirming, and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("permission.denied", "You do not have access to this school."));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "My child has left" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, they have left" }));

    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this school.");
    expect(post).toHaveBeenCalledWith("/holders/h1/left", undefined, { callerPersonId: "p1" });
  });

  it("marks a child as left", async () => {
    const post = vi.fn(async () => ok(undefined));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "My child has left" }));
    await user.click(screen.getByRole("button", { name: "Yes, they have left" }));

    await waitFor(() => expect(screen.getByText("Thandi Dlamini is marked as having left.", { exact: false })).toBeTruthy());
  });

  it("explains the erasure request, asks to confirm and says when it will be done", async () => {
    const post = vi.fn(async () => ok({ id: "e1", dueBy: "2026-11-28T10:00:00Z", completed: false }));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    expect(await screen.findByText(/at most 30 days/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Remove my child's data" }));
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Yes, remove it" }));

    await waitFor(() => expect(screen.getByText("Your request was received. It will be done by 2026-11-28.", { exact: false })).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/holders/h1/erasure-requests", undefined, { callerPersonId: "p1" });
  });

  it("says the data was removed when the server did it at once", async () => {
    const post = vi.fn(async () => ok({ id: "e1", dueBy: "2026-11-28T10:00:00Z", completed: true }));
    mount(<Requests />, { ...children, post: post as never });
    const user = userEvent.setup({ delay: null });

    await user.click(await screen.findByRole("button", { name: "Remove my child's data" }));
    await user.click(screen.getByRole("button", { name: "Yes, remove it" }));

    await waitFor(() => expect(screen.getByText("Thandi Dlamini's data has been removed.")).toBeTruthy());
  });

  it("says so when no child is linked", async () => {
    mount(<Requests />, { get: vi.fn(async () => ok([])) as never });

    expect(await screen.findByText(/No child is linked to you yet/)).toBeTruthy();
  });
});
