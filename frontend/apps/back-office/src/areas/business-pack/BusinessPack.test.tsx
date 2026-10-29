// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BusinessPack } from "./BusinessPack";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const spaza = { id: "1", code: "spaza", name: "Spaza shop", description: "A simple till", slipTemplateCode: "slip.sale", version: 1, isActive: true, features: [] };
const cafe = { id: "2", code: "cafe", name: "Cafe", description: null, slipTemplateCode: "slip.sale", version: 2, isActive: true, features: [{ flag: "tables", settingsJson: null }, { flag: "brand_new_flag", settingsJson: null }] };
const now = { shopId: "s1", packCode: "spaza", packName: "Spaza shop", packVersion: 1, slipTemplateCode: "slip.sale", features: [] };

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><BusinessPack /></SessionProvider>);
}

describe("BusinessPack", () => {
  it("shows the current pack and what each pack switches on, even flags it has no words for", async () => {
    mount({ get: vi.fn(async (path: string) => (path === "/business-packs" ? ok([spaza, cafe]) : ok(now))) as never });

    expect(await screen.findByText("Cafe")).toBeTruthy();
    expect(screen.getByText("Tables")).toBeTruthy();
    expect(screen.getByText("brand_new_flag")).toBeTruthy();
    expect(screen.getByText("No features beyond the basic till are switched on.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Choose Spaza shop" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("chooses a pack with the caller and reloads what the shop is on", async () => {
    const get = vi.fn(async (path: string) => (path === "/business-packs" ? ok([spaza, cafe]) : ok(now)));
    const put = vi.fn(async () => ok({ ...now, packCode: "cafe", packName: "Cafe" }));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Cafe");

    await user.click(screen.getByRole("button", { name: "Choose Cafe" }));

    await waitFor(() => expect(screen.getByText("Your shop now uses Cafe.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/business-pack", { callerPersonId: "p1", packCode: "cafe" });
    expect(get.mock.calls.filter((c) => c[0] === "/shops/s1/business-pack")).toHaveLength(2);
  });

  it("shows the server's refusal", async () => {
    const get = vi.fn(async (path: string) => (path === "/business-packs" ? ok([spaza, cafe]) : ok(now)));
    mount({ get: get as never, put: vi.fn(async () => fail("permission.denied", "Only the owner can change this.")) as never });
    const user = userEvent.setup();
    await screen.findByText("Cafe");
    await user.click(screen.getByRole("button", { name: "Choose Cafe" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Only the owner can change this.");
  });

  it("shows an empty list and a load error", async () => {
    mount({ get: vi.fn(async (path: string) => (path === "/business-packs" ? ok([]) : fail("network.unreachable", "We cannot reach the server."))) as never });
    expect(await screen.findByText("No business types are available.")).toBeTruthy();
    expect((await screen.findByRole("alert")).textContent).toBe("We cannot reach the server.");
  });
});
