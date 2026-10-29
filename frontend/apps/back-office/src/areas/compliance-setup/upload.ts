import type { Api } from "@tillknowit/api-client";
import type { Result } from "@tillknowit/domain";

export interface DocumentUpload {
  shopId: string;
  deviceId: string;
  staffId: string;
  typeCode: string;
  expiresOn: string;
  file: File;
}

export type DocumentUploader = (upload: DocumentUpload) => Promise<Result<{ id: string }>>;

/** POST /shops/{id}/compliance/documents takes a form upload (deviceId, staffId, typeCode, expiresOn, file); it goes through the api client's upload. */
export function uploadWith(api: Api): DocumentUploader {
  return (u) => {
    const form = new FormData();
    form.append("deviceId", u.deviceId);
    form.append("staffId", u.staffId);
    form.append("typeCode", u.typeCode);
    if (u.expiresOn !== "") form.append("expiresOn", u.expiresOn);
    form.append("file", u.file);
    return api.upload<{ id: string }>(`/shops/${encodeURIComponent(u.shopId)}/compliance/documents`, form);
  };
}
