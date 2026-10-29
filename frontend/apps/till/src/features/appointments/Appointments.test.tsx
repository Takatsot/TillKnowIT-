// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { saDate } from "../till-kit/time";
import { DEVICE, makeCtx, SHOP, STAFF } from "../till-kit/testing";
import { Appointments } from "./Appointments";
import { feature } from "./index";

afterEach(cleanup);

const cut = { id: "s1", name: "Haircut", durationMinutes: 30, priceCents: 15000, isActive: true, staffIds: [STAFF] };
const appt = (over = {}) => ({
  id: "a1", serviceId: "s1", serviceName: "Haircut", staffId: STAFF, customerId: null, customerName: "Lerato", customerPhone: "0821112222",
  startsAt: "2026-10-26T08:00:00Z", endsAt: "2026-10-26T08:30:00Z", status: "booked", hasConflict: false, priceCents: 15000, notes: null, ...over,
});

function fakeGet(appointments: unknown[], services: unknown[] = [cut]) {
  return vi.fn(async (path: string) => ok(path.endsWith("/services") ? services : appointments));
}

describe("appointments feature", () => {
  it("is switched on by the appointments flag", () => {
    expect(feature.flag).toBe("appointments");
    expect(feature.requiresOnline).toBe(true);
  });

  it("asks the server for today in South African time and lists the calendar", async () => {
    const get = fakeGet([appt()]);
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get } as never });
    render(<Appointments ctx={ctx} />);

    expect(await screen.findByText("Lerato 0821112222")).toBeTruthy();
    expect(screen.getByText("10:00 - 10:30")).toBeTruthy();   // 08:00 UTC is 10:00 in South Africa
    const call = get.mock.calls.find((c) => String(c[0]).endsWith("/appointments"))! as unknown as [string, { from: string; to: string; deviceId: string; staffId: string }];
    expect(call[1]).toMatchObject({ deviceId: DEVICE, staffId: STAFF });
    expect(call[1].from).toBe(new Date(`${saDate()}T00:00:00+02:00`).toISOString());
  });

  it("shows the empty day", async () => {
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get: fakeGet([]) as never } });
    render(<Appointments ctx={ctx} />);

    expect(await screen.findByText("No appointments on this day.")).toBeTruthy();
  });

  it("books an appointment for a service, staff member and time", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async (_p: string, _b?: unknown) => ok(appt()));
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get: fakeGet([]) as never, post } as never });
    render(<Appointments ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Book an appointment" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Service" }), "s1");
    await user.selectOptions(screen.getByRole("combobox", { name: "With" }), STAFF);
    await user.type(screen.getByLabelText("Time"), "10:30");
    await user.type(screen.getByRole("textbox", { name: "Customer name" }), "Lerato");
    await user.click(screen.getByRole("button", { name: "Book it" }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post.mock.calls[0]![0]).toBe(`/shops/${SHOP}/appointments`);
    const body = post.mock.calls[0]![1] as { startsAt: string; providerStaffId: string };
    expect(body).toMatchObject({ deviceId: DEVICE, staffId: STAFF, serviceId: "s1", providerStaffId: STAFF, customerName: "Lerato", allowConflict: false });
    expect(body.startsAt).toBe(new Date(`${saDate()}T10:30:00+02:00`).toISOString());
    expect(await screen.findByText(/Booked Haircut for Lerato/)).toBeTruthy();
  });

  it("shows the server's refusal when the slot is taken", async () => {
    const user = userEvent.setup();
    const post = vi.fn(async () => fail("appointment.conflict", "That person already has an appointment then."));
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get: fakeGet([]) as never, post } as never });
    render(<Appointments ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Book an appointment" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Service" }), "s1");
    await user.selectOptions(screen.getByRole("combobox", { name: "With" }), STAFF);
    await user.type(screen.getByLabelText("Time"), "10:30");
    await user.type(screen.getByRole("textbox", { name: "Customer name" }), "Lerato");
    await user.click(screen.getByRole("button", { name: "Book it" }));

    expect((await screen.findByRole("alert")).textContent).toBe("That person already has an appointment then.");
  });

  it("asks for the missing details before booking", async () => {
    const user = userEvent.setup();
    const post = vi.fn();
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get: fakeGet([]) as never, post } as never });
    render(<Appointments ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Book an appointment" }));
    await user.click(screen.getByRole("button", { name: "Book it" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Choose a service.");
    expect(post).not.toHaveBeenCalled();
  });

  it("confirms an appointment and only offers what fits its status", async () => {
    const user = userEvent.setup();
    const get = fakeGet([appt(), appt({ id: "a2", customerName: "Naledi", status: "completed" })]);
    const post = vi.fn(async () => ok(appt({ status: "confirmed" })));
    const { ctx } = makeCtx({ flags: ["appointments"], api: { get, post } as never });
    render(<Appointments ctx={ctx} />);

    await user.click(await screen.findByRole("button", { name: "Confirm Lerato" }));

    expect(post).toHaveBeenCalledWith(`/shops/${SHOP}/appointments/a1/action`, { deviceId: DEVICE, staffId: STAFF, action: "confirm" });
    expect(await screen.findByText("Updated: Lerato is now Confirmed.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancel Naledi" })).toBeNull();
    expect(screen.getByRole("button", { name: "Lerato did not come" })).toBeTruthy();
  });
});
