// @vitest-environment jsdom
import type { Api } from "@tillknowit/api-client";
import { fail, hashPin, MemoryOutbox, ok, type Result, type SendBatch, type UploadRequest } from "@tillknowit/domain";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { App, type AppServices } from "./App";
import { MemoryKeyValueStore } from "./localData";
import { loadDeviceConfig } from "./syncRunner";

const SHOP = "0192a3b4-0000-7000-8000-00000000b001";
const DEVICE = "0192a3b4-0000-7000-8000-00000000d001";
let pinHash: string;

beforeAll(async () => {
  pinHash = await hashPin("1234");
});
beforeEach(() => localStorage.clear());
afterEach(cleanup);

type Route = (query?: Record<string, unknown>) => Result<unknown>;

function services(routes: Record<string, Route> = {}) {
  const kv = new MemoryKeyValueStore();
  const sent: UploadRequest[] = [];
  const send: SendBatch = async (request) => {
    sent.push(request);
    return { results: request.sales.map((s) => ({ kind: "sale", id: s.id, status: "accepted" as const })) };
  };
  const all: Record<string, Route> = {
    [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: false }),
    [`GET /shops/${SHOP}/devices/${DEVICE}/staff`]: () => ok([{ staffId: "m-thandi", name: "Thandi", role: "cashier", pinHash, disabled: false }, { staffId: "m-off", name: "Left", role: "cashier", pinHash, disabled: true }]),
    [`GET /shops/${SHOP}/sync/products`]: () => ok({ products: [{ id: "p-bread", name: "Bread", barcode: "6001000000011", categoryCode: "x", costCents: 1200, priceCents: 1800, lowStockLevel: 0, expiryDate: null, isActive: true, soldByWeight: false, isAddOn: false, requiresSerial: false, parentProductId: null, variantLabel: null, updatedAt: "2026-10-26T09:00:00Z" }], asOf: "2026-10-26T10:00:00Z" }),
    [`GET /shops/${SHOP}/promotions`]: () => ok([]),
    [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => ok({ shopId: SHOP, packCode: "cafe", packName: "Cafe", packVersion: 1, slipTemplateCode: "slip.sale", features: [{ flag: "tips" }] }),
    [`GET /shops/${SHOP}/credit-accounts`]: () => ok([]),
    ...routes,
  };
  const handle = (method: string) => vi.fn(async (path: string, a?: unknown, b?: unknown) => {
    const route = all[`${method} ${path}`];
    return route ? route((method === "GET" ? a : b) as never) : fail("http.404", `no route ${method} ${path}`);
  });
  const api = { get: handle("GET"), post: handle("POST") } as unknown as Api;
  const raw = new MemoryOutbox();
  const parts: Partial<AppServices> = { kv, outbox: raw, api, send };
  return { parts, kv, sent, raw, api };
}

async function registerThisTill(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole("textbox", { name: "Shop number" }), SHOP);
  await user.type(screen.getByRole("textbox", { name: "Till number" }), DEVICE);
  await user.click(screen.getByRole("button", { name: "Register till" }));
}

async function signIn(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Thandi" }, { timeout: 4000 }));
  for (const d of "1234") await user.click(screen.getByRole("button", { name: d }));
  await user.click(screen.getByRole("button", { name: "OK" }));
  await screen.findByText("Hello, Thandi", {}, { timeout: 4000 });
}

describe("App", () => {
  it("starts on the registration screen, registers the till and then downloads who can sign in", async () => {
    const { parts } = services();
    const user = userEvent.setup();
    render(<App services={parts} />);

    expect(screen.getByRole("heading", { name: "Register this till" })).toBeTruthy();
    await registerThisTill(user);

    expect(loadDeviceConfig()).toEqual({ shopId: SHOP, deviceId: DEVICE });
    expect(await screen.findByRole("button", { name: "Thandi" }, { timeout: 4000 })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Left" })).toBeNull();   // switched off
    expect(screen.queryByRole("button", { name: "Sipho" })).toBeNull();   // no dev examples on a registered till
  });

  it("sells with the downloaded products, and sends the sale with the server's membership id as the staff id", async () => {
    const { parts, sent } = services();
    const user = userEvent.setup();
    render(<App services={parts} />);
    await registerThisTill(user);
    await signIn(user);

    await user.type(screen.getByRole("textbox", { name: "Scan or type a barcode or name" }), "6001000000011{Enter}");
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(() => expect(sent.length).toBeGreaterThan(0), { timeout: 4000 });
    expect(sent[0]).toMatchObject({ shopId: SHOP, deviceId: DEVICE, sales: [{ staffId: "m-thandi", paymentMethodCode: "card", totalCents: 1800 }] });
  });

  it("turns on what the downloaded pack switches on", async () => {
    const { parts } = services();
    const user = userEvent.setup();
    render(<App services={parts} />);
    await registerThisTill(user);
    await signIn(user);
    await user.type(screen.getByRole("textbox", { name: "Scan or type a barcode or name" }), "6001000000011{Enter}");

    expect(await screen.findByRole("group", { name: "Tip" })).toBeTruthy();   // the pack has tips on
  });

  it("keeps the sale in progress while another tab is open", async () => {
    const { parts } = services();
    const user = userEvent.setup();
    render(<App services={parts} />);
    await registerThisTill(user);
    await signIn(user);
    await user.type(screen.getByRole("textbox", { name: "Scan or type a barcode or name" }), "6001000000011{Enter}");

    await user.click(screen.getByRole("button", { name: "Cash-up" }));
    expect(screen.getByRole("textbox", { name: /^Float/ })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Sell" }));

    expect(screen.getByText("Bread")).toBeTruthy();
  });

  it("shows a banner while the till waits for the owner to approve it, and still lets the cashier sign in", async () => {
    const { parts } = services({ [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "pending", wipeRequested: false }) });
    localStorage.setItem("tillknowit.device", JSON.stringify({ shopId: SHOP, deviceId: DEVICE }));
    render(<App services={parts} />);

    expect((await screen.findByText(/waiting for the owner to approve it/, {}, { timeout: 4000 })).closest("[role=status]")).toBeTruthy();
  });

  it("shows a banner when the owner has switched the till off", async () => {
    const { parts } = services({ [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "disabled", wipeRequested: false }) });
    localStorage.setItem("tillknowit.device", JSON.stringify({ shopId: SHOP, deviceId: DEVICE }));
    render(<App services={parts} />);

    expect((await screen.findByText(/has switched this till off/, {}, { timeout: 4000 })).closest("[role=alert]")).toBeTruthy();
  });

  it("says when the last update failed and offers an update now button", async () => {
    const { parts, api } = services({ [`POST /devices/${DEVICE}/check-in`]: () => fail("network.unreachable", "We cannot reach the server.") });
    localStorage.setItem("tillknowit.device", JSON.stringify({ shopId: SHOP, deviceId: DEVICE }));
    const user = userEvent.setup();
    render(<App services={parts} />);

    expect(await screen.findByText("The last update did not work: We cannot reach the server.", {}, { timeout: 4000 })).toBeTruthy();
    expect(screen.getByText("Not updated yet")).toBeTruthy();
    const before = (api.post as unknown as { mock: { calls: unknown[] } }).mock.calls.length;
    await user.click(screen.getByRole("button", { name: "Update now" }));
    await waitFor(() => expect((api.post as unknown as { mock: { calls: unknown[] } }).mock.calls.length).toBeGreaterThan(before));
  });

  it("clears the till and goes back to registration when the owner asks for a wipe", async () => {
    const { parts, kv } = services();
    const user = userEvent.setup();
    const view = render(<App services={parts} />);
    await registerThisTill(user);
    await screen.findByRole("button", { name: "Thandi" }, { timeout: 4000 });
    expect(await kv.get("staff")).toBeTruthy();
    view.unmount();

    const wiping = services({ [`POST /devices/${DEVICE}/check-in`]: () => ok({ deviceId: DEVICE, status: "approved", wipeRequested: true }), [`POST /devices/${DEVICE}/wipe-confirmed`]: () => ok(undefined) });
    await wiping.kv.set("staff", [{ id: "x" }]);
    render(<App services={wiping.parts} />);

    expect(await screen.findByRole("heading", { name: "Register this till" }, { timeout: 4000 })).toBeTruthy();
    expect(loadDeviceConfig()).toBeNull();
    expect(await wiping.kv.get("staff")).toBeUndefined();
  });

  it("sends serial numbers that were waiting once their sale is no longer in the queue", async () => {
    const posted: string[] = [];
    const { parts, kv } = services({ [`POST /shops/${SHOP}/sales/sale-1/serials`]: () => { posted.push("sale-1"); return ok(undefined); } });
    await kv.set("serials.pending", [{ saleId: "sale-1", items: [{ productId: "p-bread", serial: "SN1" }] }]);
    const user = userEvent.setup();
    render(<App services={parts} />);
    await registerThisTill(user);
    await signIn(user);

    await waitFor(() => expect(posted).toEqual(["sale-1"]), { timeout: 4000 });
    await waitFor(async () => expect(await kv.get("serials.pending")).toEqual([]));
  });

  it("prints the shop's name from the downloaded pack on the slip", async () => {
    const printed: string[] = [];
    const { parts } = services({ [`GET /shops/${SHOP}/devices/${DEVICE}/business-pack`]: () => ok({ shopId: SHOP, packCode: "cafe", packName: "Cafe", packVersion: 1, slipTemplateCode: "slip.sale", features: [{ flag: "printer" }], shopName: "Mama Thandi Spaza" }) });
    parts.hardware = { printer: { print: async (bytes: Uint8Array) => { printed.push(new TextDecoder("latin1").decode(bytes)); } } as never };
    const user = userEvent.setup();
    render(<App services={parts} />);
    await registerThisTill(user);
    await signIn(user);

    await user.type(screen.getByRole("textbox", { name: "Scan or type a barcode or name" }), "6001000000011{Enter}");
    await user.click(screen.getByRole("button", { name: /^Pay/ }));
    await user.click(screen.getByRole("button", { name: "Card" }));

    await waitFor(() => expect(printed.join("")).toContain("Mama Thandi Spaza"), { timeout: 4000 });
  });

  it("offers the example data in a development build when the till is not registered", async () => {
    const { parts } = services();
    const user = userEvent.setup();
    render(<App services={parts} />);

    await user.click(screen.getByRole("button", { name: "Use example data (development only)" }));

    expect(await screen.findByRole("button", { name: "Thandi" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Sipho" })).toBeTruthy();
  });
});
