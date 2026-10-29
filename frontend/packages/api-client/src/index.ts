import { fail, ok, type Result } from "@tillknowit/domain";

/**
 * One way to call the server. Every call gives back a Result: the server's own error code and message when it refuses (problem+json with a "code"), and a
 * "network.unreachable" error when there is no answer, so screens handle both the same way and never see a thrown error.
 */
export type Query = Record<string, string | number | boolean | Date | null | undefined>;

export interface ApiOptions {
  baseUrl?: string;
  fetcher?: typeof fetch;
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

async function problem(response: Response): Promise<{ code: string; message: string }> {
  try {
    const body = (await response.json()) as { code?: string; title?: string; detail?: string };
    return { code: body.code ?? `http.${response.status}`, message: body.title ?? body.detail ?? `The server answered ${response.status}.` };
  } catch {
    return { code: `http.${response.status}`, message: `The server answered ${response.status}.` };
  }
}

export function createApi(options: ApiOptions = {}): Api {
  const base = (options.baseUrl ?? "/api/v1").replace(/\/$/, "");
  const fetcher = options.fetcher ?? ((...args: Parameters<typeof fetch>) => fetch(...args));

  async function send(method: string, path: string, query?: Query, body?: unknown): Promise<Response | null> {
    try {
      return await fetcher(`${base}${path}${queryString(query)}`, {
        method,
        headers: body === undefined ? undefined : { "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch {
      return null;
    }
  }

  async function form<T>(path: string, data: FormData, query?: Query): Promise<Result<T>> {
    let response: Response;
    try {
      response = await fetcher(`${base}${path}${queryString(query)}`, { method: "POST", body: data });
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
