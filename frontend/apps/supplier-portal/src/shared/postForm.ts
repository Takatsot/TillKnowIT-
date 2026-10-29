import { fail, ok, type Result } from "@tillknowit/domain";

/**
 * The shared api client only sends json, and the catalogue import is a file upload (multipart form). This is the one place that sends a form, in the same
 * Result shape the api client uses. TODO: move into @tillknowit/api-client as Api.upload when that package can be edited.
 */
export type PostForm = <T>(path: string, form: FormData) => Promise<Result<T>>;

export const postForm: PostForm = async <T>(path: string, form: FormData): Promise<Result<T>> => {
  let response: Response;
  try {
    response = await fetch(`/api/v1${path}`, { method: "POST", body: form });
  } catch {
    return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
  }
  if (!response.ok) {
    try {
      const body = (await response.json()) as { code?: string; title?: string; detail?: string };
      return fail(body.code ?? `http.${response.status}`, body.title ?? body.detail ?? `The server answered ${response.status}.`);
    } catch {
      return fail(`http.${response.status}`, `The server answered ${response.status}.`);
    }
  }
  const text = await response.text();
  return ok((text === "" ? undefined : JSON.parse(text)) as T);
};
