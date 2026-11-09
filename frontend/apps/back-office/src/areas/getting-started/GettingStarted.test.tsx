// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { GettingStarted } from "./GettingStarted";

vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const newOwner: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const owner: Session = { ...newOwner, shopId: "s1" };
const towns = [{ code: "jhb", names: { en: "City of Johannesburg" }, isActive: true }, { code: "old", names: { en: "Old town" }, isActive: false }];
const steps = (done: string[]) => ["device_check", "shop_details", "test_sale"].map((item) => ({ item, done: done.includes(item), note: null }));

function mount(api: Partial<Api>, session: Session, reload = vi.fn()) {
  render(<SessionProvider api={api as Api} initial={session}><GettingStarted reload={reload} /></SessionProvider>);
  return reload;
}

describe("GettingStarted", () => {
  it("lets a new owner create their shop with the owner named, then loads the page again", async () => {
    const post = vi.fn(async () => ok({ shopId: "new-shop" }));
    // Once the shop is made the screen switches to the progress view, which asks for the checklist.
    const get = vi.fn(async (path: string) => ok(path.endsWith("/setup") ? { steps: steps([]), allDone: false, shopStatus: "pending" } : towns));
    const reload = mount({ get: get as never, post: post as never }, newOwner);
    const user = userEvent.setup();
    await screen.findByRole("option", { name: "City of Johannesburg" });
    expect(screen.queryByRole("option", { name: "Old town" })).toBeNull();

    await user.type(screen.getByLabelText(/Shop name/), "  Mama Nomsa  ");
    await user.selectOptions(screen.getByLabelText(/Your municipality/), "jhb");
    await user.click(screen.getByRole("button", { name: "Create my shop" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops?ownerPersonId=p1", { name: "Mama Nomsa", area: "jhb", language: "en" }));
    await waitFor(() => expect(reload).toHaveBeenCalled());
  });

  it("asks for a name before calling the server, and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("shop.area_invalid", "That area is not one we know."));
    mount({ get: vi.fn(async () => ok(towns)) as never, post: post as never }, newOwner);
    const user = userEvent.setup();
    await screen.findByRole("option", { name: "City of Johannesburg" });

    await user.click(screen.getByRole("button", { name: "Create my shop" }));
    expect(screen.getByText("Type the name of your shop.")).toBeTruthy();
    expect(post).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText(/Shop name/), "Mama Nomsa");
    await user.click(screen.getByRole("button", { name: "Create my shop" }));
    expect((await screen.findByRole("alert")).textContent).toBe("That area is not one we know.");
  });

  it("shows a shop waiting for verification how far the field agent has got", async () => {
    mount({ get: vi.fn(async () => ok({ steps: steps(["device_check"]), allDone: false, shopStatus: "pending" })) as never }, owner);

    expect(await screen.findByText("1 of 3 setup steps done")).toBeTruthy();
    expect(screen.getByText("Waiting for verification")).toBeTruthy();
    expect(screen.getByText(/A TillKnowIT field agent will visit or call/)).toBeTruthy();
    expect(screen.getByText("Device check: the till or phone works")).toBeTruthy();
    expect(screen.getByText("A test sale went through")).toBeTruthy();
  });

  it("says so once the shop is verified", async () => {
    mount({ get: vi.fn(async () => ok({ steps: steps(["device_check", "shop_details", "test_sale"]), allDone: true, shopStatus: "verified" })) as never }, owner);

    expect(await screen.findByText("Your shop is verified. Everything is switched on.")).toBeTruthy();
    expect(screen.getByText("3 of 3 setup steps done")).toBeTruthy();
  });
});
