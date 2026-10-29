import { describe, expect, it, vi } from "vitest";
import { createApi, queryString } from "./index";

const respond = (status: number, body?: unknown, headers: Record<string, string> = {}) =>
  vi.fn(async () => new Response(body === undefined ? null : JSON.stringify(body), { status, headers }));

describe("queryString", () => {
  it("leaves out empty values, encodes the rest and writes dates as ISO", () => {
    expect(queryString({ a: 1, b: undefined, c: null, d: "", e: "x y", f: true, g: new Date("2026-10-26T08:00:00Z") })).toBe("?a=1&e=x%20y&f=true&g=2026-10-26T08%3A00%3A00.000Z");
    expect(queryString()).toBe("");
    expect(queryString({ a: undefined })).toBe("");
  });
});

describe("createApi", () => {
  it("returns the value of a good answer", async () => {
    const fetcher = respond(200, { id: 1 });
    const result = await createApi({ baseUrl: "/api/v1/", fetcher: fetcher as unknown as typeof fetch }).get<{ id: number }>("/plans", { audience: "shop" });

    expect(result).toEqual({ ok: true, value: { id: 1 } });
    expect(fetcher).toHaveBeenCalledWith("/api/v1/plans?audience=shop", expect.objectContaining({ method: "GET" }));
  });

  it("sends a json body with the right method and takes an empty answer as success", async () => {
    const fetcher = respond(204);
    const api = createApi({ fetcher: fetcher as unknown as typeof fetch });

    const result = await api.post("/subscriptions", { planCode: "shop_pro" });

    expect(result.ok).toBe(true);
    const [, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit];
    expect(init).toMatchObject({ method: "POST", headers: { "Content-Type": "application/json" }, body: "{\"planCode\":\"shop_pro\"}" });
    await api.put("/x");
    await api.patch("/x");
    await api.delete("/x");
    expect(fetcher.mock.calls.map((c) => (c as unknown as [string, RequestInit])[1].method)).toEqual(["POST", "PUT", "PATCH", "DELETE"]);
  });

  it("gives the servers own code and message when it refuses", async () => {
    const result = await createApi({ fetcher: respond(403, { title: "Only the owner can add branches.", code: "permission.denied", status: 403 }) as unknown as typeof fetch }).post("/x");

    expect(result).toEqual({ ok: false, code: "permission.denied", message: "Only the owner can add branches." });
  });

  it("makes a code of its own when the error has no body", async () => {
    const result = await createApi({ fetcher: respond(502) as unknown as typeof fetch }).get("/x");

    expect(result).toMatchObject({ ok: false, code: "http.502" });
  });

  it("says the server cannot be reached instead of throwing", async () => {
    const result = await createApi({ fetcher: (async () => { throw new TypeError("failed"); }) as unknown as typeof fetch }).get("/x");

    expect(result).toMatchObject({ ok: false, code: "network.unreachable" });
  });

  it("downloads a file with its name", async () => {
    const fetcher = vi.fn(async () => new Response("a,b", { status: 200, headers: { "Content-Disposition": "attachment; filename=\"sales-by-day-2026-10-31.csv\"" } }));

    const result = await createApi({ fetcher: fetcher as unknown as typeof fetch }).download("/shops/1/reports/sales-by-day");

    expect(result.ok && result.value.fileName).toBe("sales-by-day-2026-10-31.csv");
    expect(await (result.ok ? result.value.blob.text() : "")).toBe("a,b");
  });

  it("uploads a form without setting a content type and gives the same kind of result", async () => {
    const fetcher = respond(200, { created: 2 });
    const data = new FormData();
    data.append("file", new Blob(["a"]), "a.csv");

    const result = await createApi({ fetcher: fetcher as unknown as typeof fetch }).upload("/schools/1/holders/import", data, { apply: true });

    expect(result).toEqual({ ok: true, value: { created: 2 } });
    const [url, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/schools/1/holders/import?apply=true");
    expect(init.body).toBe(data);
    expect(init.headers).toBeUndefined();
  });

  it("adds the bearer token to json, upload and download requests when there is one", async () => {
    let token: string | null = "abc";
    const fetcher = vi.fn(async () => new Response("{}", { status: 200 }));
    const api = createApi({ fetcher: fetcher as unknown as typeof fetch, getToken: () => token });

    await api.get("/x");
    await api.post("/x", { a: 1 });
    await api.upload("/x", new FormData());
    await api.download("/x");
    for (const c of fetcher.mock.calls) expect((c as unknown as [string, RequestInit])[1].headers).toMatchObject({ Authorization: "Bearer abc" });
    expect((fetcher.mock.calls[1] as unknown as [string, RequestInit])[1].headers).toMatchObject({ "Content-Type": "application/json" });

    token = null;
    await api.get("/y");
    expect((fetcher.mock.calls[4] as unknown as [string, RequestInit])[1].headers).toBeUndefined();
  });
});

describe("createApi headers option", () => {
  const headersOf = (fetcher: ReturnType<typeof respond>) => (fetcher.mock.calls[0] as unknown as [string, RequestInit])[1].headers;

  it("adds the extra headers to json, upload and download requests, read fresh each time", async () => {
    let device = "one";
    const fetcher = respond(200, {});
    const api = createApi({ fetcher: fetcher as unknown as typeof fetch, headers: () => ({ "X-Device-Token": device }) });

    await api.get("/a");
    expect(headersOf(fetcher)).toEqual({ "X-Device-Token": "one" });
    device = "two";
    await api.post("/b", { x: 1 });
    expect((fetcher.mock.calls[1] as unknown as [string, RequestInit])[1].headers).toEqual({ "Content-Type": "application/json", "X-Device-Token": "two" });
    await api.upload("/c", new FormData());
    expect((fetcher.mock.calls[2] as unknown as [string, RequestInit])[1].headers).toEqual({ "X-Device-Token": "two" });
    await api.download("/d");
    expect((fetcher.mock.calls[3] as unknown as [string, RequestInit])[1].headers).toEqual({ "X-Device-Token": "two" });
  });

  it("keeps the bearer token alongside them and sends nothing extra when there are none", async () => {
    const both = respond(200, {});
    await createApi({ fetcher: both as unknown as typeof fetch, getToken: () => "abc", headers: () => ({ "X-Device-Token": "t" }) }).get("/a");
    expect(headersOf(both)).toEqual({ "X-Device-Token": "t", Authorization: "Bearer abc" });

    const none = respond(200, {});
    await createApi({ fetcher: none as unknown as typeof fetch, headers: () => ({}) }).get("/a");
    expect(headersOf(none)).toBeUndefined();
  });
});
