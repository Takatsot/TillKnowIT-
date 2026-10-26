import { Button } from "@tillknowit/design-system";
import { attemptPin, isLocked, isValidPin, type PinState } from "@tillknowit/domain";
import { translate, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import { useEffect, useState } from "react";
import { minutesLeft } from "./lock";
import styles from "./PinUnlock.module.css";
import { loadPinState, savePinState, type StaffRecord } from "./staffStore";

interface Props {
  staff: StaffRecord[];
  language: LanguageCode;
  onUnlock: (staff: StaffRecord) => void;
}

const MAX_DIGITS = 6;
const digits = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

export function PinUnlock({ staff, language, onUnlock }: Props) {
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);

  const [selected, setSelected] = useState<StaffRecord | null>(null);
  const [pin, setPin] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [pinStates, setPinStates] = useState<Record<string, PinState>>({});

  const stateOf = (s: StaffRecord): PinState => pinStates[s.id] ?? loadPinState(s.id);
  const locked = selected ? isLocked(stateOf(selected), now) : false;

  // Tick once a second while a lock is showing, so the countdown moves and the pad frees itself.
  useEffect(() => {
    if (!locked) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [locked]);

  function choose(s: StaffRecord | null) {
    setSelected(s);
    setPin("");
    setMessage("");
  }

  async function submit() {
    if (!selected || busy || !isValidPin(pin)) return;
    setBusy(true);
    const attemptedAt = Date.now();
    const result = await attemptPin(pin, selected.pinHash, stateOf(selected), attemptedAt);
    setNow(attemptedAt);
    savePinState(selected.id, result.state);
    setPinStates((s) => ({ ...s, [selected.id]: result.state }));
    setPin("");
    setBusy(false);

    if (result.status === "ok") onUnlock(selected);
    else if (result.status === "wrong") setMessage(t("unlock.wrongPin", { left: result.attemptsLeft }));
    else setMessage("");
  }

  if (staff.length === 0) return <p className={styles.notice}>{t("unlock.noStaff")}</p>;

  if (!selected) {
    return (
      <section className={styles.panel} aria-labelledby="who">
        <h1 id="who" className={styles.title}>{t("unlock.title")}</h1>
        <ul className={styles.people}>
          {staff.map((s) => (
            <li key={s.id}>
              <Button variant="secondary" className={styles.person} onClick={() => choose(s)}>{s.name}</Button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  const lockedUntil = stateOf(selected).lockedUntil;

  return (
    <section className={styles.panel} aria-labelledby="enter">
      <h1 id="enter" className={styles.title}>{t("unlock.pinFor", { name: selected.name })}</h1>

      <div
        className={styles.dots}
        role="img"
        aria-label={`${t("unlock.enterPin")}: ${pin.length}`}
      >
        {Array.from({ length: MAX_DIGITS }, (_, i) => (
          <span key={i} className={i < pin.length ? styles.dotFilled : styles.dot} />
        ))}
      </div>

      <p className={styles.message} role="status" aria-live="polite">
        {locked && lockedUntil ? t("unlock.locked", { minutes: minutesLeft(lockedUntil, now) }) : message}
      </p>

      <div className={styles.pad}>
        {digits.map((d) => (
          <Button key={d} variant="secondary" className={styles.key} disabled={locked || busy}
            onClick={() => setPin((p) => (p.length < MAX_DIGITS ? p + d : p))}>{d}</Button>
        ))}
        <Button variant="quiet" className={styles.key} disabled={locked || busy || pin === ""}
          onClick={() => setPin((p) => p.slice(0, -1))}>{t("unlock.delete")}</Button>
        <Button variant="secondary" className={styles.key} disabled={locked || busy}
          onClick={() => setPin((p) => (p.length < MAX_DIGITS ? p + "0" : p))}>0</Button>
        <Button className={styles.key} disabled={locked || busy || !isValidPin(pin)} onClick={submit}>{t("unlock.ok")}</Button>
      </div>

      <Button variant="quiet" onClick={() => choose(null)}>{t("unlock.changeStaff")}</Button>
    </section>
  );
}
