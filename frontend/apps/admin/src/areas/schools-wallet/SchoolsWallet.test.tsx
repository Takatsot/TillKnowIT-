// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SchoolsWallet } from "./SchoolsWallet";
import type { HolderUploader, School } from "./types";

afterEach(cleanup);
// These tests type into real forms; on a busy machine the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });

const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };
const school: School = {
  id: "sc1", name: "Sunrise Primary", emisNumber: null, shopId: "shop1", status: "pending", payoutSchedule: "weekly", feeBasisPoints: 250,
  staffAccountsEnabled: false, dailyCeilingCents: null, offlineSplitMinutes: 30,
};

/** A fake Api that answers by path; anything not listed is a test mistake and says so. */
function fake(routes: Record<string, unknown>) {
  const answer = (path: string) => (path in routes ? routes[path] : fail("test.unexpected", `No fake answer for ${path}`));
  return {
    get: vi.fn(async (path: string) => answer(path)),
    post: vi.fn(async (path: string) => answer(path)),
    put: vi.fn(async (path: string) => answer(path)),
  };
}

function mount(api: ReturnType<typeof fake>, upload?: HolderUploader) {
  render(<SessionProvider api={api as unknown as Api} initial={session}><SchoolsWallet upload={upload} /></SessionProvider>);
}

async function openSchool(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("School id"), "sc1");
  await user.click(screen.getByRole("button", { name: "Open school" }));
  await screen.findByText("Working on Sunrise Primary.");
}

describe("SchoolsWallet", () => {
  it("asks to open a school before the school tabs show anything", async () => {
    mount(fake({}));
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: "Holders" }));

    expect(screen.getByText("Open or register a school first.")).toBeTruthy();
  });

  it("registers a school, opens it, verifies it and saves its settings", async () => {
    const api = fake({
      "/admin/schools": ok(school),
      "/admin/schools/sc1/verify": ok({ ...school, status: "verified" }),
      "/schools/sc1/settings": ok({ ...school, status: "verified", feeBasisPoints: 300, payoutSchedule: "daily", dailyCeilingCents: 15000 }),
    });
    mount(api);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("School name"), "Sunrise Primary");
    await user.type(screen.getByLabelText("Tuckshop id"), "shop1");
    await user.click(screen.getByRole("button", { name: "Register school" }));
    expect(await screen.findByText("Sunrise Primary was registered.")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/admin/schools", { callerPersonId: "p1", name: "Sunrise Primary", emisNumber: null, shopId: "shop1" });
    expect(screen.getByText("Waiting for verification")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Verify school" }));
    expect(await screen.findByText("Sunrise Primary was verified.")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/admin/schools/sc1/verify", undefined, { callerPersonId: "p1" });
    expect(screen.queryByRole("button", { name: "Verify school" })).toBeNull();

    const fee = screen.getByLabelText(/Platform fee \(percent\)/);
    await user.clear(fee);
    await user.type(fee, "3");
    await user.selectOptions(screen.getByLabelText("Payout schedule"), "daily");
    await user.type(screen.getByLabelText(/Daily ceiling per wallet/), "150");
    await user.click(screen.getByRole("button", { name: "Save settings" }));

    expect(await screen.findByText("The settings were saved.")).toBeTruthy();
    expect(api.put).toHaveBeenCalledWith("/schools/sc1/settings", {
      callerPersonId: "p1", feeBasisPoints: 300, payoutSchedule: "daily", staffAccountsEnabled: false, dailyCeilingCents: 15000, offlineSplitMinutes: 30,
    });
  });

  it("shows the server's refusal when the school cannot be opened or registered", async () => {
    mount(fake({ "/schools/sc1": fail("school.not_found", "School not found."), "/admin/schools": fail("school.shop_taken", "That shop already belongs to a school.") }));
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("School id"), "sc1");
    await user.click(screen.getByRole("button", { name: "Open school" }));
    expect((await screen.findByRole("alert")).textContent).toBe("School not found.");

    await user.type(screen.getByLabelText("School name"), "X");
    await user.type(screen.getByLabelText("Tuckshop id"), "shop1");
    await user.click(screen.getByRole("button", { name: "Register school" }));
    await waitFor(() => expect(screen.getAllByRole("alert").map((a) => a.textContent)).toContain("That shop already belongs to a school."));
  });

  it("adds a school admin", async () => {
    const api = fake({ "/schools/sc1": ok(school), "/schools/sc1/admins": ok(undefined) });
    mount(api);
    const user = userEvent.setup();
    await openSchool(user);

    await user.type(screen.getByLabelText("Person id"), "person9");
    await user.click(screen.getByRole("button", { name: "Add admin" }));

    expect(await screen.findByText("The school admin was added.")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/schools/sc1/admins", { callerPersonId: "p1", personId: "person9" });
  });

  it("lists holders and imports a file, showing the rejected rows and the link codes once", async () => {
    const holder = { id: "h1", schoolId: "sc1", type: "learner", firstName: "Thabo", lastName: "Dlamini", grade: "4", className: "4B", status: "active", walletId: "w1", hasPhoto: false };
    const api = fake({ "/schools/sc1": ok(school), "/schools/sc1/holders": ok([holder]) });
    const upload = vi.fn<HolderUploader>(async () => ok({
      created: 1, duplicates: 0, invalid: 1,
      rows: [{ row: 2, name: "Thabo Dlamini", outcome: "created", detail: null }, { row: 3, name: "", outcome: "invalid", detail: "A first name is needed." }],
      linkCodes: [{ holderId: "h2", name: "Lerato Mokoena", grade: "5", className: "5A", linkCode: "ABCD-1234" }],
    }));
    mount(api, upload);
    const user = userEvent.setup();
    await openSchool(user);
    await user.click(screen.getByRole("tab", { name: "Holders" }));
    expect(await screen.findByText("Thabo Dlamini")).toBeTruthy();

    const file = new File(["first,last\nLerato,Mokoena"], "learners.csv", { type: "text/csv" });
    await user.upload(screen.getByLabelText("File"), file);
    await user.click(screen.getByRole("button", { name: "Import holders" }));

    expect(await screen.findByText("1 created, 0 already there, 1 not valid.")).toBeTruthy();
    expect(upload).toHaveBeenCalledWith("sc1", "p1", file);
    expect(screen.getByText("A first name is needed.")).toBeTruthy();
    expect(screen.getByText("ABCD-1234")).toBeTruthy();
    expect(screen.getByText(/shown once/)).toBeTruthy();
    expect(api.get.mock.calls.filter((c) => c[0] === "/schools/sc1/holders")).toHaveLength(2);
  });

  it("shows the server's message when the import is refused", async () => {
    const api = fake({ "/schools/sc1": ok(school), "/schools/sc1/holders": ok([]) });
    const upload = vi.fn<HolderUploader>(async () => fail("import.columns_missing", "The file needs a first name column and a surname column."));
    mount(api, upload);
    const user = userEvent.setup();
    await openSchool(user);
    await user.click(screen.getByRole("tab", { name: "Holders" }));
    expect(await screen.findByText("This school has no holders yet.")).toBeTruthy();

    await user.upload(screen.getByLabelText("File"), new File(["x"], "bad.csv", { type: "text/csv" }));
    await user.click(screen.getByRole("button", { name: "Import holders" }));

    expect((await screen.findByRole("alert")).textContent).toBe("The file needs a first name column and a surname column.");
  });

  it("saves breaks and adds a menu item", async () => {
    const api = fake({
      "/schools/sc1": ok(school),
      "/schools/sc1/menu": ok({ breaks: [{ code: "first", name: "First break", cutOff: "09:30" }], items: [] }),
      "/schools/sc1/breaks": ok(undefined),
    });
    mount(api);
    const user = userEvent.setup();
    await openSchool(user);
    await user.click(screen.getByRole("tab", { name: "Breaks and menu" }));

    const cutOff = await screen.findByLabelText("Cut-off (HH:mm) 1");
    await user.clear(cutOff);
    await user.type(cutOff, "09:45");
    await user.click(screen.getByRole("button", { name: "Save breaks" }));
    expect(await screen.findByText("The breaks were saved.")).toBeTruthy();
    expect(api.put).toHaveBeenCalledWith("/schools/sc1/breaks", { callerPersonId: "p1", breaks: [{ code: "first", name: "First break", cutOff: "09:45" }] });

    expect(screen.getByText("The menu has no items yet.")).toBeTruthy();
    api.post.mockImplementationOnce(async () => ok({ id: "m1", name: "Pie", priceCents: 1250, productId: null, categoryCode: null, isAvailable: true }));
    await user.click(screen.getByRole("button", { name: "Add a menu item" }));
    await user.type(screen.getByLabelText("Item name"), "Pie");
    await user.type(screen.getByLabelText("Price (rand)"), "12.50");
    await user.click(screen.getByRole("button", { name: "Save item" }));

    expect(await screen.findByText("Pie was saved.")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/schools/sc1/menu", { callerPersonId: "p1", name: "Pie", priceCents: 1250, productId: null, categoryCode: null, isAvailable: true });
  });

  it("records a cash deposit in cents and refuses a bad amount before asking", async () => {
    const api = fake({ "/schools/sc1": ok(school), "/admin/schools/sc1/cash-deposits": ok(undefined) });
    mount(api);
    const user = userEvent.setup();
    await openSchool(user);
    await user.click(screen.getByRole("tab", { name: "Cash and staff accounts" }));

    await user.type(screen.getByLabelText("Amount (rand)"), "abc");
    await user.type(screen.getByLabelText("Deposit reference"), "DEP-1");
    await user.click(screen.getByRole("button", { name: "Record deposit" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Write the amount in rand, for example 500.");
    expect(api.post).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText("Amount (rand)"));
    await user.type(screen.getByLabelText("Amount (rand)"), "500");
    await user.click(screen.getByRole("button", { name: "Record deposit" }));

    expect(await screen.findByText(/The deposit of .* was recorded\./)).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/admin/schools/sc1/cash-deposits", undefined, { callerPersonId: "p1", amountCents: 50000, reference: "DEP-1" });
  });

  it("shows a staff month and settles it", async () => {
    const statement = { month: "2026-10", totalOwedCents: 12000, teachers: [{ holderId: "t1", name: "Mrs Naidoo", owedCents: 12000 }] };
    const api = fake({
      "/schools/sc1": ok(school),
      "/schools/sc1/staff-accounts/2026-10": ok(statement),
      "/admin/schools/sc1/staff-accounts/2026-10/settle": ok(statement),
    });
    mount(api);
    const user = userEvent.setup();
    await openSchool(user);
    await user.click(screen.getByRole("tab", { name: "Cash and staff accounts" }));

    const month = screen.getByLabelText(/^Month/);
    await user.clear(month);
    await user.type(month, "2026-10");
    await user.click(screen.getByRole("button", { name: "Show month" }));
    expect(await screen.findByText("Mrs Naidoo")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Settle 2026-10" }));

    expect(await screen.findByText("2026-10 was settled.")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/admin/schools/sc1/staff-accounts/2026-10/settle", undefined, { callerPersonId: "p1" });
  });

  it("explains when payouts are on hold", async () => {
    const api = fake({ "/admin/wallet-payouts/run": ok({ blocked: true, blockedReason: "the reconciliation for 2026-10-26 differs by 500 cents", payouts: [] }) });
    mount(api);
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: "Payouts" }));
    await user.click(screen.getByRole("button", { name: "Run payouts" }));

    expect((await screen.findByRole("status")).textContent).toBe("Payouts are on hold: the reconciliation for 2026-10-26 differs by 500 cents");
    expect(api.post).toHaveBeenCalledWith("/admin/wallet-payouts/run", undefined, { callerPersonId: "p1", day: "" });
  });

  it("shows a shop's wallet sales and payouts", async () => {
    const api = fake({
      "/shops/shop9/wallet-payouts": ok({
        salesCents: 100000, feesCents: 2500, paidOutCents: 90000, owedToShopCents: 7500,
        payouts: [{ id: "po1", shopId: "shop9", amountCents: 90000, status: "paid", day: "2026-10-20", error: null }],
      }),
    });
    mount(api);
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: "Payouts" }));
    await user.type(screen.getByLabelText("Tuckshop id"), "shop9");
    await user.click(screen.getByRole("button", { name: "Show sales" }));

    expect(await screen.findByText("paid")).toBeTruthy();
    expect(api.get).toHaveBeenCalledWith("/shops/shop9/wallet-payouts", { callerPersonId: "p1", from: "", to: "" });
  });

  it("runs a wallet reconciliation and resolves a mismatch with a note", async () => {
    const mismatch = { id: "r1", day: "2026-10-27", partnerTotalCents: 100000, ledgerTotalCents: 99500, differenceCents: 500, status: "mismatch", note: null };
    const api = fake({ "/admin/wallet-reconciliations": ok(mismatch), "/admin/wallet-reconciliations/r1/resolve": ok({ ...mismatch, status: "resolved", note: "Partner fee found" }) });
    mount(api);
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: "Reconciliation" }));
    await user.click(screen.getByRole("button", { name: "Run reconciliation" }));
    expect(await screen.findByText("Does not match")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Resolve" }));
    expect((screen.getByRole("button", { name: "Resolve" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByLabelText(/What was found/), "Partner fee found");
    await user.click(screen.getByRole("button", { name: "Resolve" }));

    expect(await screen.findByText(/The reconciliation for 2026-10-27 was resolved\./)).toBeTruthy();
    expect(screen.getByText("Resolved")).toBeTruthy();
    expect(api.post).toHaveBeenCalledWith("/admin/wallet-reconciliations/r1/resolve", { callerPersonId: "p1", note: "Partner fee found" });
  });

  it("shows the server's refusal for reconciliation", async () => {
    mount(fake({ "/admin/wallet-reconciliations": fail("permission.denied", "You do not have access to this.") }));
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: "Reconciliation" }));
    await user.click(screen.getByRole("button", { name: "Run reconciliation" }));

    expect((await screen.findByRole("alert")).textContent).toBe("You do not have access to this.");
  });
});
