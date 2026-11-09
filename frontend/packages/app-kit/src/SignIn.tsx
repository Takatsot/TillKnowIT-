import { BrandLockup, Button, Card, Notice, TextField } from "@tillknowit/design-system";
import { languages, isLanguage } from "@tillknowit/i18n";
import { useState } from "react";
import { useAction } from "./hooks";
import { useApi, useSession, useSetSession, useSignIn, useSignOutReason, type SignInAnswer } from "./session";
import styles from "./SignIn.module.css";
import { shellText, type ShellKey } from "./shellMessages";

interface OtpRequested { message: string; devCode?: string | null }

/** Phone number, then the code we send, then signed in. Uses POST /auth/otp/request and POST /auth/otp/verify. */
export function SignIn({ signUp = false }: { signUp?: boolean }) {
  const api = useApi();
  const { language } = useSession();
  const setSession = useSetSession();
  const signIn = useSignIn();
  const reason = useSignOutReason();
  const t = (k: ShellKey, p?: Record<string, string | number>) => shellText(language, k, p);

  const [phone, setPhone] = useState("");
  const [isNew, setIsNew] = useState(false);
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"phone" | "code">("phone");
  const [devCode, setDevCode] = useState<string | null>(null);

  // Plain words for the three known refusals; anything else shows the server's own message.
  const plain = (e: { code: string; message: string }) => (e.code === "otp.phone_invalid" || e.code === "otp.too_many" || e.code === "otp.invalid" ? t(e.code) : e.message);

  const request = useAction(() => api.post<OtpRequested>("/auth/otp/request", isNew ? { phone: phone.trim(), signUpName: name.trim(), language } : { phone: phone.trim() }));
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
    <div className={styles.page}>
      <section className={styles.hero} aria-hidden="true">
        <BrandLockup size={54} inverse />
        <div className={styles.pitch}>
          <h1>{t("heroBefore")} <em>{t("heroEmphasis")}</em>.</h1>
          <p>{t("heroLead")}</p>
          <ul className={styles.points}>
            <li>{t("heroPoint1")}</li>
            <li>{t("heroPoint2")}</li>
            <li>{t("heroPoint3")}</li>
          </ul>
        </div>
        <span className={styles.foot}>{t("app")}</span>
      </section>
      <main className={styles.side}>
        <div className={styles.panel}>
          <label className={styles.language}>
            {t("language")}
            <select value={language} onChange={(e) => isLanguage(e.target.value) && setSession({ language: e.target.value })}>
              {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
            </select>
          </label>
          <Card title={t("signInTitle")}>
            {reason && <Notice kind="warning">{t(reason === "expired" ? "sessionExpired" : "sessionEnded")}</Notice>}
            {step === "phone" ? (
              <form onSubmit={(e) => { e.preventDefault(); void send(); }} className={styles.form}>
                <TextField label={t("phone")} hint={t("phoneHint")} type="tel" inputMode="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
                {isNew && <TextField label={t("yourName")} hint={t("yourNameHint")} autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} />}
                {request.error && <Notice kind="danger">{plain(request.error)}</Notice>}
                <Button type="submit" disabled={request.busy || phone.trim() === "" || (isNew && name.trim() === "")}>{t("sendCode")}</Button>
                {signUp && <Button type="button" variant="quiet" onClick={() => { setIsNew(!isNew); request.clear(); }}>{t(isNew ? "haveAccount" : "newHere")}</Button>}
              </form>
            ) : (
              <form onSubmit={(e) => { e.preventDefault(); void finish(); }} className={styles.form}>
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
        </div>
      </main>
    </div>
  );
}
