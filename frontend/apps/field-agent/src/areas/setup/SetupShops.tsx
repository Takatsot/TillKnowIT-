import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, EmptyState, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type SetupKey } from "./messages";

interface PendingShop { shopId: string; name: string; area: string | null; stepsDone: number; stepsTotal: number }
interface Checklist { steps: Array<{ item: string; done: boolean; note: string | null }>; allDone: boolean; shopStatus: string }

/** E13-1: the field agent picks a shop that is waiting, ticks off the steps as they are done at the shop, and verifies it. */
export function SetupShops() {
  const [open, setOpen] = useState<PendingShop | null>(null);
  const [message, setMessage] = useState("");
  return open
    ? <Steps shop={open} onBack={() => setOpen(null)} onVerified={(text) => { setMessage(text); setOpen(null); }} />
    : <Waiting onOpen={(s) => { setMessage(""); setOpen(s); }} message={message} />;
}

function Waiting({ onOpen, message }: { onOpen: (s: PendingShop) => void; message: string }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<PendingShop[]>("/shops/pending-verification", { agentPersonId: personId }), [personId]);
  return (
    <Card title={t("listHeading")}>
      <Stack>
        <p>{t("listHelp")}</p>
        {message && <Notice kind="success">{message}</Notice>}
        {list.error && <Notice kind="danger">{list.error.message} <Button variant="quiet" onClick={() => list.reload()}>{t("tryAgain")}</Button></Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : list.data && (list.data.length === 0 ? <EmptyState>{t("listEmpty")}</EmptyState> : (
          <Table
            rows={list.data}
            rowKey={(s) => s.shopId}
            columns={[
              { header: t("colShop"), cell: (s) => s.name },
              { header: t("colArea"), cell: (s) => s.area ?? "" },
              { header: t("colProgress"), cell: (s) => t("progress", { done: s.stepsDone, total: s.stepsTotal }) },
              { header: t("colActions"), cell: (s) => <Button variant="secondary" aria-label={t("open", { name: s.name })} onClick={() => onOpen(s)}>{t("openShort")}</Button> },
            ]}
          />
        ))}
      </Stack>
    </Card>
  );
}

function Steps({ shop, onBack, onVerified }: { shop: PendingShop; onBack: () => void; onVerified: (text: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Checklist>(`/shops/${shop.shopId}/setup`), [shop.shopId]);
  const [note, setNote] = useState("");
  const [message, setMessage] = useState("");
  const mark = useAction((item: string, text: string) => api.put<unknown>(`/shops/${shop.shopId}/setup/${encodeURIComponent(item)}`, { agentPersonId: personId, note: text.trim() === "" ? null : text.trim() }));
  const verify = useAction(() => api.post<unknown>(`/shops/${shop.shopId}/verify`, { agentPersonId: personId }));

  const label = (item: string) => (Object.hasOwn(messages.en, `step_${item}`) ? t(`step_${item}` as SetupKey) : item.replace(/_/g, " "));

  async function done(item: string) {
    setMessage("");
    const r = await mark.run(item, note);
    if (r.ok) { setNote(""); setMessage(t("stepSaved", { step: label(item) })); list.reload(); }
  }
  async function verifyShop() {
    const r = await verify.run();
    if (r.ok) onVerified(t("verified", { name: shop.name }));
  }

  const data = list.data;
  return (
    <Card title={t("stepsHeading", { name: shop.name })}>
      <Stack>
        <div><Button variant="quiet" onClick={onBack}>{t("back")}</Button></div>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !data ? <p>{t("loading")}</p> : data && (
          <>
            <TextField label={t("noteLabel")} hint={t("noteHint")} value={note} onChange={(e) => setNote(e.target.value)} />
            <Table
              rows={data.steps}
              rowKey={(s) => s.item}
              columns={[
                { header: t("colStep"), cell: (s) => label(s.item) },
                { header: t("colDone"), cell: (s) => <Badge>{s.done ? t("done") : t("notDone")}</Badge> },
                { header: t("colNote"), cell: (s) => s.note ?? "" },
                { header: t("colActions"), cell: (s) => (s.done ? "" : <Button variant="secondary" disabled={mark.busy} aria-label={t("markDoneFor", { step: label(s.item) })} onClick={() => void done(s.item)}>{t("markDone")}</Button>) },
              ]}
            />
            {message && <Notice kind="success">{message}</Notice>}
            {mark.error && <Notice kind="danger">{mark.error.message}</Notice>}
            {data.shopStatus === "verified"
              ? <Notice kind="info">{t("alreadyVerified")}</Notice>
              : <>
                  {!data.allDone && <p>{t("verifyNotReady")}</p>}
                  {verify.error && <Notice kind="danger">{verify.error.message}</Notice>}
                  <div><Button disabled={!data.allDone || verify.busy} onClick={() => void verifyShop()}>{t("verify")}</Button></div>
                </>}
          </>
        )}
      </Stack>
    </Card>
  );
}
