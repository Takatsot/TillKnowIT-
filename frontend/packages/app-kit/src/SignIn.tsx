import { Button, Card, Notice, TextField } from "@tillknowit/design-system";
import { languages, isLanguage } from "@tillknowit/i18n";
import { useState } from "react";
import { useAction } from "./hooks";
import { useApi, useSession, useSetSession, useSignIn, useSignOutReason, type SignInAnswer } from "./session";
import { shellText, type ShellKey } from "./shellMessages";

interface OtpRequested { message: string; devCode?: string | null }

/** Phone number, then the code we send, then signed in. Uses POST /auth/otp/request and POST /auth/otp/verify. */
export function SignIn() {
  const api = useApi();
  const { language } = useSession();
  const setSession = useSetSession();
  const signIn = useSignIn();
  const reason = useSignOutReason();
  const t = (k: ShellKey, p?: Record<string, string | number>) => shellText(language, k, p);

  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"phone" | "code">("phone");
  const [devCode, setDevCode] = useState<string | null>(null);

  // Plain words for the three known refusals; anything else shows the server's own message.
  const plain = (e: { code: string; message: string }) => (e.code === "otp.phone_invalid" || e.code === "otp.too_many" || e.code === "otp.invalid" ? t(e.code) : e.message);

  const request = useAction(() => api.post<OtpRequested>("/auth/otp/request", { phone: phone.trim() }));
  const verify = useAction(() => api.post<SignInAnswer>("/auth/otp/verify", { phone: phone.trim(), code: code.trim() }));

  async function send() {
    verify.clear();
    const r = await request.run();
    if (r.ok) { setDevCode(r.value?.devCode ?? null); setCode(""); setStep("code"); }
  }

  async function finish() {
    const r = await verify.run();
    if (r.ok) signIn(r.value);
  }

  return (
    <main style={{ padding: "var(--space-6)", display: "grid", gap: "var(--space-4)", alignContent: "start", maxWidth: "32rem", margin: "0 auto" }}>
      <strong>{t("app")}</strong>
      <label style={{ display: "grid", gap: "var(--space-1)", fontSize: "var(--font-size-small)" }}>
        {t("language")}
        <select value={language} onChange={(e) => isLanguage(e.target.value) && setSession({ language: e.target.value })}>
          {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
        </select>
      </label>
      <Card title={t("signInTitle")}>
        {reason && <Notice kind="warning">{t(reason === "expired" ? "sessionExpired" : "sessionEnded")}</Notice>}
        {step === "phone" ? (
          <form onSubmit={(e) => { e.preventDefault(); void send(); }} style={{ display: "grid", gap: "var(--space-3)" }}>
            <TextField label={t("phone")} hint={t("phoneHint")} type="tel" inputMode="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
            {request.error && <Notice kind="danger">{plain(request.error)}</Notice>}
            <Button type="submit" disabled={request.busy || phone.trim() === ""}>{t("sendCode")}</Button>
          </form>
        ) : (
          <form onSubmit={(e) => { e.preventDefault(); void finish(); }} style={{ display: "grid", gap: "var(--space-3)" }}>
            <Notice kind="info">{t("codeSent")}</Notice>
            {devCode && (
              <Notice kind="warning">
                <strong>{t("devOnly")}</strong>{" "}{t("devCode", { code: devCode })}
              </Notice>
            )}
            <TextField label={t("code")} hint={t("codeHint")} inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
            {verify.error && <Notice kind="danger">{plain(verify.error)}</Notice>}
            <Button type="submit" disabled={verify.busy || code.trim() === ""}>{t("signInButton")}</Button>
            <Button type="button" variant="quiet" onClick={() => { setStep("phone"); setDevCode(null); verify.clear(); }}>{t("differentNumber")}</Button>
          </form>
        )}
      </Card>
    </main>
  );
}
