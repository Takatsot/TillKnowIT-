import { formatDate, formatRand, useAction, useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Notice, Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { ChildScope, mutedStyle, type ChildContext } from "../../shared/ChildScope";
import { messages, type RequestsKey } from "./messages";

interface Refund {
  id: string;
  walletId: string;
  amountCents: number;
  status: string;
}

interface Erasure {
  id: string;
  dueBy: string;
  completed: boolean;
}

export function Requests() {
  return <ChildScope render={(ctx) => <Body key={ctx.child.holderId} ctx={ctx} />} />;
}

function Body({ ctx }: { ctx: ChildContext }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const { child } = ctx;
  const [refundNote, setRefundNote] = useState("");
  const [erasureSure, setErasureSure] = useState(false);
  const [erasureNote, setErasureNote] = useState("");

  const refund = useAction(() => api.post<Refund>(`/wallets/${child.walletId}/refund-requests`, { callerPersonId: personId }));
  const erase = useAction(() => api.post<Erasure>(`/holders/${child.holderId}/erasure-requests`, undefined, { callerPersonId: personId }));

  const statusText = (status: string) => {
    const key = `refundstatus${status.toLowerCase()}` as RequestsKey;
    return key in messages.en ? t(key) : status;
  };

  return (
    <Stack>
      <Card title={t("refundHeading")}>
        <p style={mutedStyle}>{t("refundHelp")}</p>
        <p style={mutedStyle}>{t("leftAskSchool")}</p>
        {refund.error && <Notice kind="danger">{refund.error.message}</Notice>}
        {refundNote && <Notice kind="success">{refundNote}</Notice>}
        <Button disabled={refund.busy} onClick={() => { setRefundNote(""); void refund.run().then((r) => { if (r.ok) setRefundNote(t("refundSent", { amount: formatRand(r.value.amountCents), status: statusText(r.value.status) })); }); }}>{t("refundGo")}</Button>
      </Card>

      <Card title={t("erasureHeading")}>
        <p style={mutedStyle}>{t("erasureHelp")}</p>
        {erase.error && <Notice kind="danger">{erase.error.message}</Notice>}
        {erasureNote && <Notice kind="success">{erasureNote}</Notice>}
        {!erasureSure ? <Button variant="secondary" onClick={() => { setErasureSure(true); setErasureNote(""); }}>{t("erasureGo")}</Button> : (
          <Notice kind="warning">
            <Stack>
              <span>{t("erasureSure", { name: child.name })}</span>
              <Button
                disabled={erase.busy}
                onClick={() => void erase.run().then((r) => {
                  setErasureSure(false);
                  if (!r.ok) return;
                  setErasureNote(r.value.completed ? t("erasureDone", { name: child.name }) : t("erasureWaiting", { date: formatDate(r.value.dueBy) }));
                  ctx.reload();
                })}
              >
                {t("erasureYes")}
              </Button>
              <Button variant="secondary" onClick={() => setErasureSure(false)}>{t("erasureNo")}</Button>
            </Stack>
          </Notice>
        )}
      </Card>
    </Stack>
  );
}
