import { Button, SelectField, TextField } from "@tillknowit/design-system";
import { attemptPin, isLocked, isValidPin, type CatalogueItem } from "@tillknowit/domain";
import { translateArea, type AreaMessages, type LanguageCode } from "@tillknowit/i18n";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { KeyValueStore } from "./localData";
import { loadPinState, loadStaff, savePinState, type StaffRecord } from "./staffStore";

/** Small pieces shared by the till's core tabs (stock, refunds, cash-up, credit book, transfers). Each tab keeps its own texts in its own messages file. */

export type Translate<K extends string> = (key: K, params?: Record<string, string | number>) => string;

export function useT<K extends string>(messages: AreaMessages<K>, language: LanguageCode): Translate<K> {
  return useMemo(() => (key: K, params?: Record<string, string | number>) => translateArea(messages, language, key, params), [messages, language]);
}

/** A whole number typed by a person, or null. */
export const wholeNumber = (text: string): number | null => (/^\d{1,9}$/.test(text.trim()) ? Number(text.trim()) : null);

/** Runs a save one at a time: while it runs `busy` is true, so a double tap never saves twice. */
export function useBusy(): { busy: boolean; run: <T>(work: () => Promise<T>) => Promise<T | undefined> } {
  const [busy, setBusy] = useState(false);
  const live = useRef(true);
  const running = useRef(false);
  useEffect(() => () => { live.current = false; }, []);
  const run = useCallback(async <T,>(work: () => Promise<T>): Promise<T | undefined> => {
    if (running.current) return undefined;
    running.current = true;
    setBusy(true);
    try {
      return await work();
    } finally {
      running.current = false;
      if (live.current) setBusy(false);
    }
  }, []);
  return { busy, run };
}

interface PickerProps {
  catalogue: readonly CatalogueItem[];
  label: string;
  onPick: (item: CatalogueItem) => void;
  /** Shown when what was typed matches nothing. */
  notFound: string;
}

/** Find a product by scanning its barcode or typing part of its name. Enter takes an exact barcode, or the only match. */
export function ProductPicker({ catalogue, label, onPick, notFound }: PickerProps) {
  const [text, setText] = useState("");
  const [missed, setMissed] = useState(false);
  const q = text.trim().toLowerCase();
  const matches = useMemo(() => (q.length < 2 ? [] : catalogue.filter((c) => c.name.toLowerCase().includes(q) || c.barcode === q).slice(0, 6)), [q, catalogue]);

  function choose(item: CatalogueItem) {
    setText("");
    setMissed(false);
    onPick(item);
  }

  function submit() {
    const exact = catalogue.find((c) => c.barcode !== null && c.barcode === text.trim());
    const only = matches.length === 1 ? matches[0] : undefined;
    const item = exact ?? only;
    if (item) choose(item);
    else setMissed(q !== "");
  }

  return (
    <div>
      <form onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <TextField label={label} value={text} autoComplete="off" onChange={(e) => { setText(e.target.value); setMissed(false); }} error={missed ? notFound : null} />
      </form>
      {matches.length > 0 && (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-2)" }}>
          {matches.map((m) => <li key={m.id}><Button variant="secondary" onClick={() => choose(m)}>{m.name}</Button></li>)}
        </ul>
      )}
    </div>
  );
}

export interface ApprovalTexts {
  who: string;
  pin: string;
  approve: string;
  cancel: string;
  wrong: (left: number) => string;
  locked: string;
  nobody: string;
}

interface ApprovalProps {
  kv: KeyValueStore | undefined;
  texts: ApprovalTexts;
  onApproved: (managerId: string) => void;
  onCancel: () => void;
}

/**
 * A manager or the owner approves something by their PIN, checked on the till against the downloaded PIN hash, with the same lock after wrong tries as the
 * sign-in screen. Works with no connection. (The server checks the approver's role again when the record is sent.)
 */
export function ManagerApproval({ kv, texts, onApproved, onCancel }: ApprovalProps) {
  const [managers, setManagers] = useState<StaffRecord[] | null>(null);
  const [who, setWho] = useState("");
  const [pin, setPin] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void loadStaff(kv).then((all) => {
      const list = all.filter((s) => s.role === "manager" || s.role === "owner");
      setManagers(list);
      setWho(list[0]?.id ?? "");
    });
  }, [kv]);

  async function submit() {
    const manager = managers?.find((m) => m.id === who);
    if (!manager || busy || !isValidPin(pin)) return;
    setBusy(true);
    const now = Date.now();
    const state = loadPinState(manager.id);
    if (isLocked(state, now)) {
      setBusy(false);
      return setMessage(texts.locked);
    }
    const result = await attemptPin(pin, manager.pinHash, state, now);
    savePinState(manager.id, result.state);
    setPin("");
    setBusy(false);
    if (result.status === "ok") onApproved(manager.id);
    else setMessage(result.status === "wrong" ? texts.wrong(result.attemptsLeft) : texts.locked);
  }

  if (managers !== null && managers.length === 0) {
    return (
      <div>
        <p role="alert">{texts.nobody}</p>
        <Button variant="quiet" onClick={onCancel}>{texts.cancel}</Button>
      </div>
    );
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); void submit(); }} style={{ display: "grid", gap: "var(--space-3)" }}>
      <SelectField label={texts.who} value={who} onChange={(e) => setWho(e.target.value)}>
        {(managers ?? []).map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
      </SelectField>
      <TextField label={texts.pin} type="password" inputMode="numeric" autoComplete="off" value={pin} maxLength={6} onChange={(e) => setPin(e.target.value.replace(/\D/g, ""))} error={message || null} />
      <div style={{ display: "flex", gap: "var(--space-3)" }}>
        <Button variant="quiet" onClick={onCancel}>{texts.cancel}</Button>
        <Button type="submit" disabled={busy || !isValidPin(pin) || who === ""}>{texts.approve}</Button>
      </div>
    </form>
  );
}
