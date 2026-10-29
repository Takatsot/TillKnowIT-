import { fail, ok, type Result } from "@tillknowit/domain";

export type PhotoUploader = (holderId: string, callerPersonId: string, file: File) => Promise<Result<void>>;

/**
 * PUT /holders/{id}/photo takes a form upload (fields callerPersonId and file). The shared api client only sends JSON, so this one call goes
 * through fetch directly and still answers with a Result, never a thrown error.
 */
export const uploadPhoto: PhotoUploader = async (holderId, callerPersonId, file) => {
  const form = new FormData();
  form.append("callerPersonId", callerPersonId);
  form.append("file", file);
  let response: Response;
  try {
    response = await fetch(`/api/v1/holders/${encodeURIComponent(holderId)}/photo`, { method: "PUT", body: form });
  } catch {
    return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
  }
  if (response.ok) return ok(undefined);
  try {
    const body = (await response.json()) as { code?: string; title?: string };
    return fail(body.code ?? `http.${response.status}`, body.title ?? `The server answered ${response.status}.`);
  } catch {
    return fail(`http.${response.status}`, `The server answered ${response.status}.`);
  }
};
