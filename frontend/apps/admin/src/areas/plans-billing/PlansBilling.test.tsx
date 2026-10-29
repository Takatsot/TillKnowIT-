// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlansBilling } from "./PlansBilling";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const pro = { id: "a", code: "shop_pro", audience: "shop", name: "Pro", priceCents: 19900, isActive: true, features: [{ feature: "messages", limit: 500 }, { feature: "multi_branch", limit: null }] };
const free = { id: "b", code: "shop_free", audience: "shop", name: "Free", priceCents: 0, isActive: true, features: [] };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><PlansBilling /></SessionProvider>);
}

describe("PlansBilling", () => {
  it("says the prices are placeholders and lists plans with prices and limits", async () => {
    mount({ get: vi.fn(async () => ok([free, pro])) as never });

    expect(await screen.findByText("Pro")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("placeholders");
    expect(screen.getByText(/messages \(up to 500\), multi_branch/)).toBeTruthy();
    expect(screen.getAllByText("Free").length).toBeGreaterThan(0);
  });

  it("changes a price and a limit and saves the whole plan", async () => {
    const put = vi.fn(async () => ok({ ...pro, priceCents: 24900 }));
    const get = vi.fn(async () => ok([pro]));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Pro");

    await user.click(screen.getByRole("button", { name: "Edit Pro (shop_pro)" }));
    const price = screen.getByLabelText("Price a month (rand)");
    await user.clear(price);
    await user.type(price, "249");
    const limit = screen.getByLabelText("Limit for messages");
    await user.clear(limit);
    await user.type(limit, "800");
    await user.click(screen.getByRole("button", { name: "Save plan" }));

    await waitFor(() => expect(screen.getByText("Pro was saved.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/plans", {
      callerPersonId: "p1", code: "shop_pro", audience: "shop", name: "Pro", priceCents: 24900, isActive: true,
      features: [{ feature: "messages", limit: 800 }, { feature: "multi_branch", limit: null }],
    });
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("does not send a price that is not rand", async () => {
    const put = vi.fn(async () => ok(pro));
    mount({ get: vi.fn(async () => ok([pro])) as never, put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Pro");

    await user.click(screen.getByRole("button", { name: "Edit Pro (shop_pro)" }));
    const price = screen.getByLabelText("Price a month (rand)");
    await user.clear(price);
    await user.type(price, "abc");
    await user.click(screen.getByRole("button", { name: "Save plan" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Write the price in rand, for example 199 or 199.50.");
    expect(put).not.toHaveBeenCalled();
  });

  it("shows the server's refusal when saving", async () => {
    mount({ get: vi.fn(async () => ok([pro])) as never, put: vi.fn(async () => fail("permission.denied", "Only a platform admin can manage plans.")) as never });
    const user = userEvent.setup();
    await screen.findByText("Pro");

    await user.click(screen.getByRole("button", { name: "Edit Pro (shop_pro)" }));
    await user.click(screen.getByRole("button", { name: "Save plan" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Only a platform admin can manage plans.");
  });

  it("shows an empty state", async () => {
    mount({ get: vi.fn(async () => ok([])) as never });

    expect(await screen.findByText("There are no plans.")).toBeTruthy();
  });
});
