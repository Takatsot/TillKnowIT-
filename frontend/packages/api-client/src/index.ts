import { fail, ok, type Result } from "@tillknowit/domain";

/**
 * One way to call the server. Every call gives back a Result: the server's own error code and message when it refuses (problem+json with a "code"), and a
 * "network.unreachable" error when there is no answer, so screens handle both the same way and never see a thrown error.
 */
export type Query = Record<string, string | number | boolean | Date | null | undefined>;

export interface ApiOptions {
  baseUrl?: string;
  fetcher?: typeof fetch;
  /** The signed-in person's access token, read on every request. When it returns a token, `Authorization: Bearer <token>` is added. */
  getToken?: () => string | null | undefined;
  /** Extra headers read on every request (JSON, upload and download), for example a till's `X-Device-Token`. Authorization and Content-Type win if they clash. */
  headers?: () => Record<string, string>;
}

export interface Download {
  blob: Blob;
  fileName: string;
}

export interface Api {
  get<T>(path: string, query?: Query): Promise<Result<T>>;
  post<T>(path: string, body?: unknown, query?: Query): Promise<Result<T>>;
  put<T>(path: string, body?: unknown, query?: Query): Promise<Result<T>>;
  patch<T>(path: string, body?: unknown, query?: Query): Promise<Result<T>>;
  delete<T>(path: string, query?: Query): Promise<Result<T>>;
  /** A file upload (multipart form). The browser sets the content type and boundary. */
  upload<T>(path: string, form: FormData, query?: Query): Promise<Result<T>>;
  download(path: string, query?: Query): Promise<Result<Download>>;
}

export function queryString(query?: Query): string {
  if (!query) return "";
  const parts: string[] = [];
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === "") continue;
    const text = value instanceof Date ? value.toISOString() : String(value);
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(text)}`);
  }
  return parts.length === 0 ? "" : `?${parts.join("&")}`;
}

/** Shown when a part of the app is not switched on for the shop. The screens show this softly, not as a failure. */
export const FEATURE_OFF = "This part is not switched on for your shop yet. Change your business type to add it.";

/** Shown when the app was published without an API address, so its requests land on the page's own host (which only serves files). */
export const NO_API_CONFIGURED = "This copy of the app has no server connected yet, so it cannot sign in. Ask whoever published it to set the API address.";

async function problem(response: Response): Promise<{ code: string; message: string }> {
  if ((response.status === 404 || response.status === 405) && API_BASE === "/api/v1") {
    const fromHtmlHost = !(response.headers.get("content-type") ?? "").includes("json");
    if (fromHtmlHost) return { code: "api.not_configured", message: NO_API_CONFIGURED };
  }
  try {
    const body = (await response.json()) as { code?: string; title?: string; detail?: string };
    const code = body.code ?? `http.${response.status}`;
    // Not an error: the shop's business type simply does not include this part. Say so in plain words (the server's own message names an internal feature code).
    if (code === "feature.not_enabled") return { code, message: FEATURE_OFF };
    return { code, message: body.title ?? body.detail ?? `The server answered ${response.status}.` };
  } catch {
    return { code: `http.${response.status}`, message: `The server answered ${response.status}.` };
  }
}

const headersOrUndefined = (h: Record<string, string>) => (Object.keys(h).length === 0 ? undefined : h);

/**
 * Where the API is. In development the dev server forwards /api/v1 to the API; when the apps are hosted somewhere else (GitHub Pages), the build is given
 * the API's address in VITE_API_URL (for example https://tillknowit-api.example.com), and the path is added here.
 */
export const API_BASE: string = (() => {
  const env = (import.meta as unknown as { env?: Record<string, string | undefined> }).env;
  const host = (env?.["VITE_API_URL"] ?? "").trim().replace(/\/$/, "");
  return `${host}/api/v1`;
})();

export function createApi(options: ApiOptions = {}): Api {
  const base = (options.baseUrl ?? API_BASE).replace(/\/$/, "");
  const fetcher = options.fetcher ?? ((...args: Parameters<typeof fetch>) => fetch(...args));

  function authHeaders(): Record<string, string> {
    const token = options.getToken?.();
    const extra = options.headers?.() ?? {};
    return token ? { ...extra, Authorization: `Bearer ${token}` } : { ...extra };
  }

  async function send(method: string, path: string, query?: Query, body?: unknown): Promise<Response | null> {
    try {
      return await fetcher(`${base}${path}${queryString(query)}`, {
        method,
        headers: body === undefined ? headersOrUndefined(authHeaders()) : { "Content-Type": "application/json", ...authHeaders() },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch {
      return null;
    }
  }

  async function form<T>(path: string, data: FormData, query?: Query): Promise<Result<T>> {
    let response: Response;
    try {
      response = await fetcher(`${base}${path}${queryString(query)}`, { method: "POST", body: data, headers: headersOrUndefined(authHeaders()) });
    } catch {
      return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
    }
    if (!response.ok) {
      const p = await problem(response);
      return fail(p.code, p.message);
    }
    const text = response.status === 204 ? "" : await response.text();
    return ok((text === "" ? undefined : JSON.parse(text)) as T);
  }

  async function json<T>(method: string, path: string, query?: Query, body?: unknown): Promise<Result<T>> {
    const response = await send(method, path, query, body);
    if (!response) return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
    if (!response.ok) {
      const p = await problem(response);
      return fail(p.code, p.message);
    }
    if (response.status === 204) return ok(undefined as T);
    const text = await response.text();
    return ok((text === "" ? undefined : JSON.parse(text)) as T);
  }

  return {
    get: (path, query) => json("GET", path, query),
    post: (path, body, query) => json("POST", path, query, body ?? {}),
    put: (path, body, query) => json("PUT", path, query, body ?? {}),
    patch: (path, body, query) => json("PATCH", path, query, body ?? {}),
    delete: (path, query) => json("DELETE", path, query),
    upload: form,
    async download(path, query) {
      const response = await send("GET", path, query);
      if (!response) return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
      if (!response.ok) {
        const p = await problem(response);
        return fail(p.code, p.message);
      }
      const disposition = response.headers.get("Content-Disposition") ?? "";
      const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1];
      return ok({ blob: await response.blob(), fileName: name ? decodeURIComponent(name) : "download" });
    },
  };
}
