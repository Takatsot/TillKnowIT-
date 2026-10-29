import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import type { Holder, Token } from "./types";

/** A person's cards and wristbands: link a new one (tap it on a reader that types the number, or type the number printed on it) and block a lost one. */
export function HolderCards({ holder, onClose }: { holder: Holder; onClose: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const name = `${holder.firstName} ${holder.lastName}`;
  const list = useLoad(() => api.get<Token[]>(`/holders/${holder.id}/tokens`, { callerPersonId: personId }), [holder.id, personId]);
  const [number, setNumber] = useState("");
  const [label, setLabel] = useState("");
  const [note, setNote] = useState("");
  const issue = useAction(() => api.post<Token>(`/holders/${holder.id}/tokens`, { callerPersonId: personId, type: "nfc", rawToken: number.trim(), label: label.trim() || null }));
  const block = useAction((id: string) => api.post<void>(`/tokens/${id}/block`, undefined, { callerPersonId: personId }));

  async function submit() {
    setNote("");
    const r = await issue.run();
    if (r.ok) {
      setNumber("");
      setLabel("");
      setNote(t("cardLinked", { name }));
      list.reload();
    }
  }

  async function blockOne(token: Token) {
    setNote("");
    const r = await block.run(token.id);
    if (r.ok) {
      setNote(t("cardBlockedNote"));
      list.reload();
    }
  }

  return (
    <Card title={t("cardsHeading", { name })}>
      <Stack>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <Inline>
              <TextField label={t("cardNumber")} hint={t("cardNumberHint")} value={number} onChange={(e) => setNumber(e.target.value)} />
              <TextField label={t("cardName")} hint={t("cardNameHint")} maxLength={40} value={label} onChange={(e) => setLabel(e.target.value)} />
              <Button type="submit" disabled={issue.busy || number.trim() === ""}>{t("cardLink")}</Button>
            </Inline>
            {issue.error && <Notice kind="danger">{issue.error.message}</Notice>}
          </Stack>
        </form>
        {block.error && <Notice kind="danger">{block.error.message}</Notice>}
        {note && <Notice kind="success">{note}</Notice>}
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        <Table
          rows={list.data ?? []}
          rowKey={(c) => c.id}
          empty={t("cardsEmpty")}
          columns={[
            { header: t("cardName"), cell: (c) => c.label ?? t("cardUnnamed") },
            { header: t("colStatus"), cell: (c) => <Badge>{c.status === "active" ? t("cardActive") : t("cardBlocked")}</Badge> },
            {
              header: t("colActions"),
              cell: (c) => c.status === "active"
                ? <Button variant="quiet" disabled={block.busy} aria-label={t("cardBlockFor", { card: c.label ?? t("cardUnnamed") })} onClick={() => void blockOne(c)}>{t("cardBlock")}</Button>
                : null,
            },
          ]}
        />
        <Inline><Button variant="quiet" onClick={onClose}>{t("cardsClose")}</Button></Inline>
      </Stack>
    </Card>
  );
}
