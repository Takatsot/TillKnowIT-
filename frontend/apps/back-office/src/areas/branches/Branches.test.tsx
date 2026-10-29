// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Branches } from "./Branches";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const head = { shopId: "s1", name: "Thandi's Spaza", isHead: true, status: "verified", municipalityCode: "JHB" };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Branches /></SessionProvider>);
}

describe("Branches", () => {
  it("lists the head shop and its branches", async () => {
    mount({ get: vi.fn(async () => ok([head, { ...head, shopId: "s2", name: "Soweto branch", isHead: false, status: "pending" }])) as never });

    expect(await screen.findByText("Soweto branch")).toBeTruthy();
    expect(screen.getByText("Head shop")).toBeTruthy();
    expect(screen.getByText("Branch")).toBeTruthy();
  });

  it("adds a branch, says so and reloads the list", async () => {
    const get = vi.fn(async () => ok([head]));
    const post = vi.fn(async () => ok({ ...head, shopId: "s2", name: "Alex", isHead: false }));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Thandi's Spaza");

    await user.type(screen.getByLabelText("Branch name"), "Alex");
    await user.click(screen.getByRole("button", { name: "Add branch" }));

    await waitFor(() => expect(screen.getByText("Alex was added.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/organisations/s1/branches", { callerPersonId: "p1", name: "Alex", area: null });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("explains the plan when branches are not included and shows other errors as they are", async () => {
    const post = vi.fn(async () => fail("plan.feature_not_included", "Branches are not part of your plan."));
    mount({ get: vi.fn(async () => ok([head])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Thandi's Spaza");

    await user.type(screen.getByLabelText("Branch name"), "Alex");
    await user.click(screen.getByRole("button", { name: "Add branch" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Branches are part of the Pro plan.");
  });

  it("shows the server's message when the list cannot be loaded", async () => {
    mount({ get: vi.fn(async () => fail("permission.denied", "You are not allowed to see the branches.")) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("You are not allowed to see the branches.");
  });
});
