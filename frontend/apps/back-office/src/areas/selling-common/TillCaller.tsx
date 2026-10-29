import { useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, TextField } from "@tillknowit/design-system";
import { useState, type ReactNode } from "react";
import { messages } from "./messages";

export interface TillCaller {
  deviceId: string;
  staffId: string;
  ready: boolean;
  /** The small card where the person can change the stand-ins. Render it once on the screen. */
  card: ReactNode;
}

/**
 * Many selling routes want a till (deviceId) and a cashier (staffId) instead of a person. TODO(auth): until sign-in exists the till is the session's device and the
 * cashier is the signed-in person. The server may well refuse that stand-in (staff.unknown); the card lets the person type real ids.
 */
export function useTillCaller(): TillCaller {
  const session = useSession();
  const t = useMessages(messages);
  const [committed, setCommitted] = useState({ deviceId: session.deviceId, staffId: session.personId });
  const [draft, setDraft] = useState(committed);
  const card = (
    <Card title={t("heading")}>
      <p>{t("hint")}</p>
      <form onSubmit={(e) => { e.preventDefault(); setCommitted({ deviceId: draft.deviceId.trim(), staffId: draft.staffId.trim() }); }}>
        <Inline>
          <TextField label={t("deviceId")} value={draft.deviceId} onChange={(e) => setDraft({ ...draft, deviceId: e.target.value })} />
          <TextField label={t("staffId")} value={draft.staffId} onChange={(e) => setDraft({ ...draft, staffId: e.target.value })} />
          <Button type="submit" variant="secondary">{t("use")}</Button>
        </Inline>
      </form>
    </Card>
  );
  return { ...committed, ready: committed.deviceId !== "" && committed.staffId !== "", card };
}
