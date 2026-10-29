// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Packs, featureFlags } from "./Packs";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const spaza = {
  id: "k1", code: "spaza", name: "Spaza shop", description: null, slipTemplateCode: "slip.sale", version: 3, isActive: true,
  features: [{ flag: "weight", settingsJson: null }, { flag: "price_embedded_barcodes", settingsJson: "{\"prefix\":\"2\"}" }],
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={api as Api} initial={session}><Packs /></SessionProvider>);
}

describe("Packs", () => {
  it("knows the 24 feature flags, including school_wallet", () => {
    expect(featureFlags).toHaveLength(24);
    expect(featureFlags).toContain("school_wallet");
  });

  it("lists packs with version and feature count", async () => {
    mount({ get: vi.fn(async () => ok([spaza])) as never });

    expect(await screen.findByText("Spaza shop")).toBeTruthy();
    expect(screen.getByText("3")).toBeTruthy();
    expect(screen.getByText("Active")).toBeTruthy();
  });

  it("edits a pack: ticks a feature, keeps the settings and says the new version", async () => {
    const put = vi.fn(async () => ok({ ...spaza, version: 4 }));
    const get = vi.fn(async () => ok([spaza]));
    mount({ get: get as never, put: put as never });
    const user = userEvent.setup();
    await screen.findByText("Spaza shop");

    await user.click(screen.getByRole("button", { name: "Edit Spaza shop" }));
    expect((screen.getByLabelText("Settings for price_embedded_barcodes (JSON object, optional)") as HTMLTextAreaElement).value).toBe("{\"prefix\":\"2\"}");
    await user.click(screen.getByLabelText("refunds"));
    await user.click(screen.getByRole("button", { name: "Save pack" }));

    await waitFor(() => expect(screen.getByText("Spaza shop was saved (version 4).")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/admin/business-packs/spaza", expect.objectContaining({
      callerPersonId: "p1",
      features: [{ flag: "refunds", settingsJson: null }, { flag: "weight", settingsJson: null }, { flag: "price_embedded_barcodes", settingsJson: "{\"prefix\":\"2\"}" }],
    }));
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("creates a pack and shows the server's refusal", async () => {
    const post = vi.fn(async () => fail("pack.code_taken", "That pack code is already used."));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    expect(await screen.findByText("There are no business packs yet.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "New pack" }));
    await user.type(screen.getByLabelText(/Pack code/), "cafe");
    await user.type(screen.getByLabelText("Pack name"), "Cafe");
    await user.click(screen.getByRole("button", { name: "Create pack" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That pack code is already used.");
    expect(post).toHaveBeenCalledWith("/admin/business-packs", expect.objectContaining({ code: "cafe", name: "Cafe", features: [] }));
  });

  it("offers the school wallet flag with its hint and sends it when ticked", async () => {
    const post = vi.fn(async () => ok({ ...spaza, code: "tuckshop", name: "Tuckshop", version: 1 }));
    mount({ get: vi.fn(async () => ok([])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("There are no business packs yet.");

    await user.click(screen.getByRole("button", { name: "New pack" }));
    expect(screen.getByText("Turn on for shops that are school tuckshops.")).toBeTruthy();
    await user.type(screen.getByLabelText(/Pack code/), "tuckshop");
    await user.type(screen.getByLabelText("Pack name"), "Tuckshop");
    await user.click(screen.getByLabelText("School wallet (school_wallet)"));
    await user.click(screen.getByRole("button", { name: "Create pack" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith("/admin/business-packs", expect.objectContaining({ features: [{ flag: "school_wallet", settingsJson: null }] }));
  });

  it("switches a pack off with the active query", async () => {
    const post = vi.fn(async () => ok({ ...spaza, isActive: false }));
    mount({ get: vi.fn(async () => ok([spaza])) as never, post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Spaza shop");

    await user.click(screen.getByRole("button", { name: "Switch off Spaza shop" }));

    await waitFor(() => expect(screen.getByText("Spaza shop was switched off.")).toBeTruthy());
    expect(post).toHaveBeenCalledWith("/admin/business-packs/spaza/active", undefined, { callerPersonId: "p1", active: false });
  });

  it("shows the server's message when the list cannot be loaded", async () => {
    mount({ get: vi.fn(async () => fail("permission.denied", "Only a platform admin can manage packs.")) as never });

    expect((await screen.findByRole("alert")).textContent).toBe("Only a platform admin can manage packs.");
  });
});
