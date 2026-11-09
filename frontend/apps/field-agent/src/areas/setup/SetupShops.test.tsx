// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SetupShops } from "./SetupShops";

vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "agent1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const shops = [{ shopId: "s1", name: "Mama Nomsa", area: "jhb", stepsDone: 6, stepsTotal: 7 }];
const items = ["device_check", "training_signoff"];
const checklist = (done: string[], status = "pending") => ({
  steps: items.map((item) => ({ item, done: done.includes(item), note: item === "device_check" && done.includes(item) ? "Till works" : null })),
  allDone: done.length === items.length,
  shopStatus: status,
});

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><SetupShops /></SessionProvider>);
}

describe("SetupShops", () => {
  it("lists the shops waiting, asking as the signed-in agent", async () => {
    const get = vi.fn(async () => ok(shops));
    mount({ get: get as never });

    expect(await screen.findByText("Mama Nomsa")).toBeTruthy();
    expect(screen.getByText("6 of 7")).toBeTruthy();
    expect(get).toHaveBeenCalledWith("/shops/pending-verification", { agentPersonId: "agent1" });
  });

  it("shows an empty list kindly and a refusal plainly", async () => {
    mount({ get: vi.fn(async () => ok([])) as never });
    expect(await screen.findByText("No shops are waiting for setup.")).toBeTruthy();
    cleanup();
    mount({ get: vi.fn(async () => fail("permission.denied", "Only a field agent can see the shops waiting for setup.")) as never });
    expect(await screen.findByText(/Only a field agent can see the shops/)).toBeTruthy();
  });

  it("marks a step done with the note, and will not verify until every step is done", async () => {
    const put = vi.fn(async () => ok(undefined));
    const get = vi.fn(async (path: string) => ok(path.endsWith("/setup") ? checklist(["device_check"]) : shops));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Open Mama Nomsa" }));

    expect(((await screen.findByRole("button", { name: "Verify this shop" })) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Every step must be done before you can verify the shop.")).toBeTruthy();
    await user.type(screen.getByLabelText(/Note for the next step/), "  Owner showed how  ");
    await user.click(screen.getByRole("button", { name: "Mark Training sign-off done" }));

    await waitFor(() => expect(put).toHaveBeenCalledWith("/shops/s1/setup/training_signoff", { agentPersonId: "agent1", note: "Owner showed how" }));
    expect(await screen.findByText("Training sign-off is marked done.")).toBeTruthy();
  });

  it("verifies a finished shop and goes back to the list with a message", async () => {
    const post = vi.fn(async () => ok(undefined));
    const get = vi.fn(async (path: string) => ok(path.endsWith("/setup") ? checklist(items) : shops));
    mount({ get: get as never, post: post as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Open Mama Nomsa" }));

    await user.click(await screen.findByRole("button", { name: "Verify this shop" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/verify", { agentPersonId: "agent1" }));
    expect(await screen.findByText("Mama Nomsa is verified.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Open Mama Nomsa" })).toBeTruthy();
  });
});
