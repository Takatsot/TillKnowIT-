/** Fails early, with the reason, if the API is not running. */
export default async function globalSetup() {
  const api = process.env["API_URL"] ?? "http://localhost:5080";
  try {
    const r = await fetch(`${api}/api/v1/auth/otp/request`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ phone: "0000" }) });
    if (r.status >= 500) throw new Error(`answered ${r.status}`);
  } catch (e) {
    throw new Error(`The API is not reachable at ${api} (${(e as Error).message}). Start it first: see frontend/e2e/README.md.`);
  }
}
