// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { withCaller } from "../selling-common/testing";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ServicesAppointments } from "./ServicesAppointments";

// Typing into forms is slow on a busy machine; the default 5 seconds is too tight.
vi.setConfig({ testTimeout: 30_000 });
afterEach(cleanup);

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const cut = { id: "sv1", name: "Haircut", durationMinutes: 30, priceCents: 15000, isActive: true, staffIds: [] };
const appt = {
  id: "a1", serviceId: "sv1", serviceName: "Haircut", staffId: "p1", customerId: null, customerName: "Zanele", customerPhone: "0821234567",
  startsAt: "2026-11-02T09:00:00+02:00", endsAt: "2026-11-02T09:30:00+02:00", status: "booked", hasConflict: false, priceCents: 15000, notes: null,
};

function mount(api: Partial<Api>) {
  render(<SessionProvider api={withCaller(api)} initial={session}><ServicesAppointments /></SessionProvider>);
}

const get = (h: Partial<Record<"services" | "appointments" | "rules", unknown>> = {}) => vi.fn(async (path: string, _query?: Record<string, unknown>) => {
  if (path.endsWith("/services")) return h.services ?? ok([cut]);
  if (path.endsWith("/appointments")) return h.appointments ?? ok([appt]);
  if (path.endsWith("/commission-rules")) return h.rules ?? ok([]);
  return fail("x", `unexpected ${path}`);
}) as never;

async function openCalendar(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Appointments" }));
  fireEvent.change(await screen.findByLabelText("Day"), { target: { value: "2026-11-02" } });
}

describe("Services and appointments", () => {
  it("lists services and says when there are none", async () => {
    mount({ get: get() });
    expect(await screen.findByText("Haircut")).toBeTruthy();
    cleanup();
    mount({ get: get({ services: ok([]) }) });
    expect(await screen.findByText("No services yet.")).toBeTruthy();
  });

  it("saves a service with the price in cents and the staff ids", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok(cut));
    mount({ get: get(), post: post as never });
    const user = userEvent.setup();
    await screen.findByText("Haircut");
    await user.type(screen.getByLabelText("Service name"), "Beard trim");
    await user.clear(screen.getByLabelText("Duration in minutes"));
    await user.type(screen.getByLabelText("Duration in minutes"), "15");
    await user.type(screen.getByLabelText("Price in rand"), "60");
    await user.type(screen.getByLabelText(/Staff ids who do it/), "m1, m2");
    await user.click(screen.getByRole("button", { name: "Save service" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/services", { callerPersonId: "p1", name: "Beard trim", durationMinutes: 15, priceCents: 6000, isActive: true, staffIds: ["m1", "m2"] }));
  });

  it("shows the day's appointments for the South African day and confirms one", async () => {
    const g = get();
    const post = vi.fn(async (..._a: unknown[]) => ok({ ...appt, status: "confirmed" }));
    mount({ get: g, post: post as never });
    const user = userEvent.setup();
    await openCalendar(user);
    expect(await screen.findByText("Zanele (0821234567)")).toBeTruthy();
    expect((g as unknown as ReturnType<typeof vi.fn>).mock.calls.some((c) => (c[1] as Record<string, string>)?.from === "2026-11-02T00:00:00+02:00" && (c[1] as Record<string, string>).to === "2026-11-03T00:00:00+02:00")).toBe(true);

    await user.click(screen.getByRole("button", { name: "Confirm Zanele" }));
    await waitFor(() => expect(post).toHaveBeenCalledWith("/shops/s1/appointments/a1/action", { deviceId: "d1", staffId: "p1", action: "confirm" }));
    expect(await screen.findByText("Zanele is now Confirmed.")).toBeTruthy();
  });

  it("shows an empty day and a server refusal when the booking clashes", async () => {
    const post = vi.fn(async () => fail("appointment.conflict", "That time is already taken."));
    mount({ get: get({ appointments: ok([]) }), post: post as never });
    const user = userEvent.setup();
    await openCalendar(user);
    expect(await screen.findByText("No appointments on this day.")).toBeTruthy();

    await user.selectOptions(screen.getByLabelText("Service"), "sv1");
    await user.type(screen.getByLabelText("Customer name"), "Zanele");
    await user.click(screen.getByRole("button", { name: "Book it" }));
    expect((await screen.findByRole("alert")).textContent).toBe("That time is already taken.");
  });

  it("books and reschedules", async () => {
    const post = vi.fn(async (..._a: unknown[]) => ok(appt));
    const patch = vi.fn(async (..._a: unknown[]) => ok(appt));
    mount({ get: get(), post: post as never, patch: patch as never });
    const user = userEvent.setup();
    await openCalendar(user);
    await screen.findByText("Zanele (0821234567)");

    await user.selectOptions(screen.getByLabelText("Service"), "sv1");
    await user.type(screen.getByLabelText("Customer name"), "Thabo");
    await user.click(screen.getByRole("button", { name: "Book it" }));
    await waitFor(() => expect(post).toHaveBeenCalled());
    const body = post.mock.calls[0]![1] as Record<string, unknown>;
    expect(body).toMatchObject({ deviceId: "d1", staffId: "p1", serviceId: "sv1", providerStaffId: "p1", customerName: "Thabo", startsAt: "2026-11-02T09:00:00+02:00" });
    expect(typeof body.id).toBe("string");

    await user.click(screen.getByRole("button", { name: "Reschedule Zanele" }));
    fireEvent.change(screen.getByLabelText("New time for Zanele"), { target: { value: "10:30" } });
    await user.click(screen.getByRole("button", { name: "Save new time for Zanele" }));
    await waitFor(() => expect(patch).toHaveBeenCalledWith("/shops/s1/appointments/a1", { deviceId: "d1", staffId: "p1", startsAt: "2026-11-02T10:30:00+02:00", providerStaffId: null }));
  });

  it("saves a commission rule and downloads the report", async () => {
    const put = vi.fn(async (..._a: unknown[]) => ok({ id: "r1", staffId: null, serviceId: null, percent: 25 }));
    const download = vi.fn(async (..._a: unknown[]) => ok({ blob: new Blob(["x"]), fileName: "commission.csv" }));
    URL.createObjectURL = vi.fn(() => "blob:x");
    URL.revokeObjectURL = vi.fn();
    mount({ get: get(), put: put as never, download: download as never });
    const user = userEvent.setup();
    await screen.findByText("Haircut");
    await user.click(screen.getByRole("button", { name: "Commission" }));
    expect(await screen.findByText("No commission rules yet.")).toBeTruthy();

    await user.clear(screen.getByLabelText("Percent (0 to 100)"));
    await user.type(screen.getByLabelText("Percent (0 to 100)"), "25");
    await user.click(screen.getByRole("button", { name: "Save rule" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith("/shops/s1/commission-rules", { callerPersonId: "p1", staffId: null, serviceId: null, percent: 25 }));

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-11-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-11-30" } });
    await user.click(screen.getByRole("button", { name: "Download report" }));
    await waitFor(() => expect(download).toHaveBeenCalledWith("/shops/s1/reports/commission", { callerPersonId: "p1", from: "2026-11-01", to: "2026-11-30", format: "csv" }));
    expect(await screen.findByText("The report commission.csv was downloaded.")).toBeTruthy();
  });
});
