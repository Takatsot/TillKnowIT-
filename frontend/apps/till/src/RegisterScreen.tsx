import { Button, TextField } from "@tillknowit/design-system";
import type { LanguageCode } from "@tillknowit/i18n";
import { useState } from "react";
import { clearDeviceToken, saveEnrolmentCode } from "./deviceToken";
import { checkRegistration, saveDeviceConfig } from "./registration";
import styles from "./RegisterScreen.module.css";
import type { DeviceConfig } from "./syncRunner";
import { tm } from "./tillMessages";

interface Props {
  language: LanguageCode;
  onRegistered: (device: DeviceConfig) => void;
  /** Development builds only: carry on with the example staff and products. */
  onSkip?: () => void;
  /** Set when the server refused this till's credential: the same form, with the old ids filled in and a note that the queued sales are kept. */
  again?: { reason: "code" | "again"; shopId: string; deviceId: string; waiting: number };
}

/**
 * First run: the owner gets a shop number and a till number in the back office and types them in here. Nothing is checked with the server at this point (the till
 * may be offline); the first update tells the till whether the owner has approved it yet.
 */
export function RegisterScreen({ language, onRegistered, onSkip, again }: Props) {
  const [shopId, setShopId] = useState(again?.shopId ?? "");
  const [deviceId, setDeviceId] = useState(again?.deviceId ?? "");
  const [code, setCode] = useState("");
  const [errors, setErrors] = useState<{ shop?: string; device?: string; save?: string }>({});

  function submit() {
    const checked = checkRegistration(shopId, deviceId);
    if (!checked.ok) {
      return setErrors(checked.error === "shop" ? { shop: tm(language, "reg.shopInvalid") } : { device: tm(language, "reg.deviceInvalid") });
    }
    // Only the till's identity and credential are replaced here: the queue of sales is never touched.
    clearDeviceToken();
    if (!saveDeviceConfig(checked.device) || !saveEnrolmentCode(code.trim())) return setErrors({ save: tm(language, "reg.saveFailed") });
    setErrors({});
    onRegistered(checked.device);
  }

  return (
    <section className={styles.panel} aria-labelledby="register">
      <h1 id="register" className={styles.title}>{tm(language, again ? "reg.again.title" : "reg.title")}</h1>
      <p className={styles.intro}>{tm(language, again ? (again.reason === "code" ? "reg.again.code" : "reg.again.lost") : "reg.introCode")}</p>
      {again && again.waiting > 0 && <p className={styles.intro}>{tm(language, "reg.again.queue", { count: again.waiting })}</p>}
      <form className={styles.form} onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <TextField label={tm(language, "reg.shopId")} value={shopId} error={errors.shop} autoComplete="off" autoCapitalize="none" spellCheck={false} onChange={(e) => setShopId(e.target.value)} />
        <TextField label={tm(language, "reg.deviceId")} value={deviceId} error={errors.device} autoComplete="off" autoCapitalize="none" spellCheck={false} onChange={(e) => setDeviceId(e.target.value)} />
        <TextField label={tm(language, "reg.code")} value={code} autoComplete="off" autoCapitalize="none" spellCheck={false} onChange={(e) => setCode(e.target.value)} />
        {errors.save && <p role="alert" className={styles.error}>{errors.save}</p>}
        <Button type="submit" disabled={shopId.trim() === "" || deviceId.trim() === "" || code.trim() === ""}>{tm(language, again ? "reg.again.save" : "reg.save")}</Button>
      </form>
      {onSkip && <Button variant="quiet" onClick={onSkip}>{tm(language, "reg.skipDev")}</Button>}
    </section>
  );
}
