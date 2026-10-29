// @vitest-environment jsdom
import { fail, ok } from "@tillknowit/domain";
import type { Api } from "@tillknowit/api-client";
import { SessionProvider, type Session } from "@tillknowit/app-kit";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ComplianceSetup } from "./ComplianceSetup";
import type { DocumentUploader } from "./upload";

afterEach(cleanup);
vi.setConfig({ testTimeout: 30000 });

const session: Session = { personId: "p1", shopId: "s1", supplierOrganisationId: "", deviceId: "d1", language: "en" };
const checklist = {
  steps: [
    { item: "device_check", done: true, completedAt: "2026-10-01T08:00:00Z", note: null },
    { item: "test_sale", done: false, completedAt: null, note: null },
  ],
  allDone: false,
  shopStatus: "pending",
};
const types = [{ code: "trading_licence", names: { en: "Business or trading licence" }, isActive: true }];
const doc = { id: "doc1", typeCode: "trading_licence", fileName: "licence.pdf", expiresOn: "2026-11-10", daysLeft: 12, status: "expiring_soon" };

function routes(over: Record<string, unknown> = {}) {
  return vi.fn(async (path: string) => {
    if (path in over) return over[path];
    if (path === "/shops/s1/setup") return ok(checklist);
    if (path === "/shops/s1/compliance/documents") return ok([doc]);
    if (path === "/lookups/document_types") return ok(types);
    return fail("test.unexpected", path);
  });
}

function mount(api: Partial<Api>, upload: DocumentUploader = vi.fn(async () => ok({ id: "new" })), save = vi.fn()) {
  render(<SessionProvider api={api as Api} initial={session}><ComplianceSetup upload={upload} save={save} /></SessionProvider>);
  return { upload, save };
}

describe("ComplianceSetup", () => {
  it("shows the checklist and marks a step done", async () => {
    const put = vi.fn(async () => ok(undefined));
    mount({ get: routes() as never, put: put as never });
    const user = userEvent.setup();

    expect(await screen.findByText("Device check")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Mark Test sale as done" }));

    await waitFor(() => expect(screen.getByText("Test sale was marked as done.")).toBeTruthy());
    expect(put).toHaveBeenCalledWith("/shops/s1/setup/test_sale", { agentPersonId: "p1", note: null });
  });

  it("shows the server's refusal when a step cannot be saved", async () => {
    const put = vi.fn(async () => fail("permission.denied", "Only a field agent can complete this step."));
    mount({ get: routes() as never, put: put as never });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Mark Test sale as done" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Only a field agent can complete this step.");
  });

  it("lists documents with their status and shows the empty state", async () => {
    mount({ get: routes() as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Compliance documents" }));
    expect(await screen.findByText("licence.pdf")).toBeTruthy();
    expect(screen.getByText("Expiring soon")).toBeTruthy();
    cleanup();

    mount({ get: routes({ "/shops/s1/compliance/documents": ok([]) }) as never });
    await userEvent.setup().click(await screen.findByRole("button", { name: "Compliance documents" }));
    expect(await screen.findByText("No documents uploaded yet.")).toBeTruthy();
  });

  it("checks the form, then uploads a file with the type and expiry", async () => {
    const { upload } = mount({ get: routes() as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Compliance documents" }));
    await screen.findByText("licence.pdf");

    await user.click(screen.getByRole("button", { name: "Upload document" }));
    expect(screen.getByText("Choose what kind of document this is.")).toBeTruthy();
    expect(upload).not.toHaveBeenCalled();

    const file = new File(["%PDF"], "cert.pdf", { type: "application/pdf" });
    await user.selectOptions(screen.getByLabelText("Document type"), "trading_licence");
    await user.upload(screen.getByLabelText("File"), file);
    await user.type(screen.getByLabelText("Expiry date (optional)"), "2027-01-31");
    await user.click(screen.getByRole("button", { name: "Upload document" }));

    await waitFor(() => expect(screen.getByText("cert.pdf was uploaded.")).toBeTruthy());
    expect(upload).toHaveBeenCalledWith({ shopId: "s1", deviceId: "d1", staffId: "p1", typeCode: "trading_licence", expiresOn: "2027-01-31", file });
  });

  it("shows the server's message when an upload is refused", async () => {
    const upload: DocumentUploader = vi.fn(async () => fail("file.too_large", "The file is larger than 10 MB."));
    mount({ get: routes() as never }, upload);
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Compliance documents" }));
    await screen.findByText("licence.pdf");

    await user.selectOptions(screen.getByLabelText("Document type"), "trading_licence");
    await user.upload(screen.getByLabelText("File"), new File(["x"], "big.pdf", { type: "application/pdf" }));
    await user.click(screen.getByRole("button", { name: "Upload document" }));

    expect((await screen.findByRole("alert")).textContent).toBe("The file is larger than 10 MB.");
  });

  it("deletes a document with the device and staff ids", async () => {
    const del = vi.fn(async () => ok(undefined));
    mount({ get: routes() as never, delete: del as never });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Compliance documents" }));

    await user.click(await screen.findByRole("button", { name: "Delete licence.pdf" }));

    await waitFor(() => expect(screen.getByText("licence.pdf was deleted.")).toBeTruthy());
    expect(del).toHaveBeenCalledWith("/shops/s1/compliance/documents/doc1", { deviceId: "d1", staffId: "p1" });
  });
});
