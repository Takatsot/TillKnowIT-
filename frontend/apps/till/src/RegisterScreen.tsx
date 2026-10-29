import { Button, TextField } from "@tillknowit/design-system";
import type { LanguageCode } from "@tillknowit/i18n";
import { useState } from "react";
import { checkRegistration, saveDeviceConfig } from "./registration";
import styles from "./RegisterScreen.module.css";
import type { DeviceConfig } from "./syncRunner";
import { tm } from "./tillMessages";

interface Props {
  language: LanguageCode;
  onRegistered: (device: DeviceConfig) => void;
  /** Development builds only: carry on with the example staff and products. */
  onSkip?: () => void;
}

/**
 * First run: the owner gets a shop number and a till number in the back office and types them in here. Nothing is checked with the server at this point (the till
 * may be offline); the first update tells the till whether the owner has approved it yet.
 */
export function RegisterScreen({ language, onRegistered, onSkip }: Props) {
  const [shopId, setShopId] = useState("");
  const [deviceId, setDeviceId] = useState("");
  const [errors, setErrors] = useState<{ shop?: string; device?: string; save?: string }>({});

  function submit() {
    const checked = checkRegistration(shopId, deviceId);
    if (!checked.ok) {
      return setErrors(checked.error === "shop" ? { shop: tm(language, "reg.shopInvalid") } : { device: tm(language, "reg.deviceInvalid") });
    }
    if (!saveDeviceConfig(checked.device)) return setErrors({ save: tm(language, "reg.saveFailed") });
    setErrors({});
    onRegistered(checked.device);
  }

  return (
    <section className={styles.panel} aria-labelledby="register">
      <h1 id="register" className={styles.title}>{tm(language, "reg.title")}</h1>
      <p className={styles.intro}>{tm(language, "reg.intro")}</p>
      <form className={styles.form} onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <TextField label={tm(language, "reg.shopId")} value={shopId} error={errors.shop} autoComplete="off" autoCapitalize="none" spellCheck={false} onChange={(e) => setShopId(e.target.value)} />
        <TextField label={tm(language, "reg.deviceId")} value={deviceId} error={errors.device} autoComplete="off" autoCapitalize="none" spellCheck={false} onChange={(e) => setDeviceId(e.target.value)} />
        {errors.save && <p role="alert" className={styles.error}>{errors.save}</p>}
        <Button type="submit" disabled={shopId.trim() === "" || deviceId.trim() === ""}>{tm(language, "reg.save")}</Button>
      </form>
      {onSkip && <Button variant="quiet" onClick={onSkip}>{tm(language, "reg.skipDev")}</Button>}
    </section>
  );
}
