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

  it("updates only the details that were filled in", async () => {
    const patch = vi.fn(async () => ok({}));
    mount(<Children />, { get: vi.fn(async () => ok([thandi])) as never, patch: patch as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Change details or photo" }));

    await user.type(screen.getByLabelText("Class"), "5C");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Details saved.")).toBeTruthy());
    expect(patch).toHaveBeenCalledWith("/holders/h1", { callerPersonId: "p1", firstName: null, lastName: null, grade: null, className: "5C" });
  });

  it("uploads a photo and shows the server's refusal", async () => {
    const upload = vi.fn()
      .mockResolvedValueOnce(fail("permission.denied", "You do not have access to this school."))
      .mockResolvedValueOnce(ok(undefined));
    mount(<Children upload={upload} />, { get: vi.fn(async () => ok([thandi])) as never });
    const user = userEvent.setup({ delay: null });
    await user.click(await screen.findByRole("button", { name: "Change details or photo" }));

    await user.click(screen.getByRole("button", { name: "Upload photo" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Choose a photo first.");

    const file = new File(["x"], "face.png", { type: "image/png" });
    await user.upload(screen.getByLabelText("Photo"), file);
    await user.click(screen.getByRole("button", { name: "Upload photo" }));
    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this school.");

    await user.click(screen.getByRole("button", { name: "Upload photo" }));
    await waitFor(() => expect(screen.getByText("Photo saved.")).toBeTruthy());
    expect(upload).toHaveBeenCalledWith("h1", "p1", file);
  });
});
