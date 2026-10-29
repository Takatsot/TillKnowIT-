// @vitest-environment jsdom
import { ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useTillCaller } from "./TillCaller";
import "./testing";

afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "", language: "en" };
const device = (id: string, name: string, status: string) => ({ deviceId: id, name, status, lastSeenAt: null, wipeRequested: false });

function Probe() {
  const c = useTillCaller();
  return <div>{c.card}<p data-testid="who">{`${c.deviceId}|${c.staffId}|${c.ready}`}</p></div>;
}

function mount(routes: Record<string, unknown>) {
  const get = vi.fn(async (path: string) => (path in routes ? routes[path] : ok([])) as never);
  render(<SessionProvider api={{ get } as unknown as Api} initial={session}><Probe /></SessionProvider>);
  return get;
}

describe("useTillCaller", () => {
  it("uses the first approved till and the person's own membership at this shop", async () => {
    const get = mount({
      "/shops/s1/devices": ok([device("d0", "Pending till", "pending"), device("d1", "Counter 1", "approved")]),
      "/people/p1/memberships": ok([{ membershipId: "mOther", organisationId: "s9" }, { membershipId: "m1", organisationId: "s1" }]),
    });
    expect((await screen.findByTestId("who")).textContent).toBe("d1|m1|true");
    expect(screen.getByText("You are acting as yourself (staff id m1).")).toBeTruthy();
    expect(get).not.toHaveBeenCalledWith("/shops/s1/staff", expect.anything());
    expect(screen.queryByText("Pending till")).toBeNull();
  });

  it("falls back to the staff list when the person has no membership here", async () => {
    mount({
      "/shops/s1/devices": ok([device("d1", "Counter 1", "approved")]),
      "/people/p1/memberships": ok([]),
      "/shops/s1/staff": ok([{ staffId: "m5", name: "Lindiwe", phone: "", role: "cashier", disabled: true }, { staffId: "m6", name: "Zanele", phone: "", role: "cashier", disabled: false }]),
    });
    expect(await screen.findByText("You have no staff membership at this shop, so choose a staff member below.")).toBeTruthy();
    expect((await screen.findByTestId("who")).textContent).toBe("d1|m6|true");
    expect(screen.queryByText("Lindiwe")).toBeNull();
  });

  it("says so, and is not ready, when there is no approved till", async () => {
    mount({ "/shops/s1/devices": ok([device("d0", "Pending till", "pending")]), "/people/p1/memberships": ok([{ membershipId: "m1", organisationId: "s1" }]) });
    expect(await screen.findByText(/no approved till yet/)).toBeTruthy();
    expect(screen.getByTestId("who").textContent).toBe("|m1|false");
  });
});
