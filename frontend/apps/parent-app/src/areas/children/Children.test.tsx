// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mount, rand, thandi } from "../../shared/testing";
import { Children } from "./Children";

afterEach(cleanup);

describe("Children", () => {
  it("lists my children with their balance", async () => {
    mount(<Children />, { get: vi.fn(async () => ok([thandi])) as never });

    expect(await screen.findByText("Thandi Dlamini")).toBeTruthy();
    expect(screen.getByText(rand("R50,00"), { exact: false })).toBeTruthy();
    expect(screen.getByText("Sunrise Primary", { exact: false })).toBeTruthy();
  });

  it("says so when no child is linked", async () => {
    mount(<Children />, { get: vi.fn(async () => ok([])) as never });

    expect(await screen.findByText("No child is linked to you yet. Add one below.")).toBeTruthy();
  });

  it("shows the server's message when the list cannot be loaded", async () => {
    mount(<Children />, { get: vi.fn(async () => fail("network.unreachable", "We cannot reach the server.")) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
  });

  it("links a child with the code and consent, then reloads", async () => {
    const get = vi.fn(async () => ok([]));
    const post = vi.fn(async () => ok(thandi));
    mount(<Children />, { get: get as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("No child is linked to you yet. Add one below.");

    const add = screen.getByRole("button", { name: "Add child" }) as HTMLButtonElement;
    expect(add.disabled).toBe(true);
    await user.type(screen.getByLabelText("Link code"), "ABCD2345");
    await user.click(screen.getByRole("checkbox"));
    await user.click(add);

    await waitFor(() => expect(screen.getByText("Thandi Dlamini was added.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/guardianships", { parentPersonId: "p1", linkCode: "ABCD2345", consent: true, consentVersion: "parent-consent-v1" });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("shows the server's refusal for a wrong code", async () => {
    const post = vi.fn(async () => fail("link.code_invalid", "That code is not valid. Check it against the card from the school."));
    mount(<Children />, { get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup({ delay: null });
    await screen.findByText("No child is linked to you yet. Add one below.");

    await user.type(screen.getByLabelText("Link code"), "WRONG123");
    await user.click(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("button", { name: "Add child" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That code is not valid. Check it against the card from the school.");
  });

  it("tells the parent the school makes changes, and offers no edit or photo upload", async () => {
    mount(<Children />, { get: vi.fn(async () => ok([thandi])) as never });

    expect(await screen.findByText(/are all done by the school/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Change details or photo" })).toBeNull();
    expect(screen.queryByLabelText("Photo")).toBeNull();
  });
});
