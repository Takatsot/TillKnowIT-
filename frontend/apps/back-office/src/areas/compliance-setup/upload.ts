import { fail, ok, type Result } from "@tillknowit/domain";

export interface DocumentUpload {
  shopId: string;
  deviceId: string;
  staffId: string;
  typeCode: string;
  expiresOn: string;
  file: File;
}

export type DocumentUploader = (upload: DocumentUpload) => Promise<Result<{ id: string }>>;

/**
 * POST /shops/{id}/compliance/documents takes a form upload (deviceId, staffId, typeCode, expiresOn, file). The shared api client only sends JSON,
 * so this one call goes through fetch directly and still answers with a Result, never a thrown error.
 */
export const uploadDocument: DocumentUploader = async (u) => {
  const form = new FormData();
  form.append("deviceId", u.deviceId);
  form.append("staffId", u.staffId);
  form.append("typeCode", u.typeCode);
  if (u.expiresOn !== "") form.append("expiresOn", u.expiresOn);
  form.append("file", u.file);
  let response: Response;
  try {
    response = await fetch(`/api/v1/shops/${encodeURIComponent(u.shopId)}/compliance/documents`, { method: "POST", body: form });
  } catch {
    return fail("network.unreachable", "We cannot reach the server. Check the connection and try again.");
  }
  if (response.ok) {
    try {
      return ok((await response.json()) as { id: string });
    } catch {
      return ok({ id: "" });
    }
  }
  try {
    const body = (await response.json()) as { code?: string; title?: string };
    return fail(body.code ?? `http.${response.status}`, body.title ?? `The server answered ${response.status}.`);
  } catch {
    return fail(`http.${response.status}`, `The server answered ${response.status}.`);
  }
};
