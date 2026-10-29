import { useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Notice, SelectField, Stack } from "@tillknowit/design-system";
import { useState, type CSSProperties, type ReactNode } from "react";
import { messages } from "./messages";
import type { Child } from "./types";

export const listStyle: CSSProperties = { listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-3)" };
export const itemStyle: CSSProperties = { display: "grid", gap: "var(--space-1)", padding: "var(--space-3)", border: "1px solid var(--color-border)", borderRadius: "var(--radius)" };
export const bigStyle: CSSProperties = { fontSize: "var(--font-size-xlarge)", fontWeight: 700, margin: 0 };
export const mutedStyle: CSSProperties = { color: "var(--color-text-muted)", margin: 0 };
export const wrapStyle: CSSProperties = { overflowWrap: "anywhere" };

export interface ChildContext {
  child: Child;
  children: Child[];
  reload: () => void;
}

/**
 * Loads the parent's children (GET /me/children) and lets them pick one. Every area but "My children" works on one child at a time,
 * so loading, errors, the empty state and the picker live here once.
 */
export function ChildScope({ render }: { render: (ctx: ChildContext) => ReactNode }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Child[]>("/me/children", { callerPersonId: personId }), [personId]);
  const [pickedId, setPickedId] = useState("");

  if (list.error) {
    return (
      <Stack>
        <Notice kind="danger">{list.error.message}</Notice>
        <Button variant="secondary" onClick={list.reload}>{t("reload")}</Button>
      </Stack>
    );
  }
  if (!list.data) return <p>{t("loading")}</p>;
  if (list.data.length === 0) return <Notice>{t("noChildren")}</Notice>;
  const child = list.data.find((c) => c.holderId === pickedId) ?? list.data[0]!;

  return (
    <Stack>
      {list.data.length > 1 && (
        <SelectField label={t("which")} value={child.holderId} onChange={(e) => setPickedId(e.target.value)}>
          {list.data.map((c) => <option key={c.holderId} value={c.holderId}>{c.name}</option>)}
        </SelectField>
      )}
      {render({ child, children: list.data, reload: list.reload })}
    </Stack>
  );
}
