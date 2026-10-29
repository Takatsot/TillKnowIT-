import { formatDateTime, formatRand, saveFile, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import qrcode from "qrcode-generator";
import { useEffect, useState } from "react";
import { ChildScope, itemStyle, listStyle, mutedStyle } from "../../shared/ChildScope";
import type { Child, StatementLine, Token } from "../../shared/types";
import { messages, type StatementKey } from "./messages";

export function Statement() {
  return <ChildScope render={({ child }) => <Body key={child.walletId} child={child} />} />;
}

function Body({ child }: { child: Child }) {
  return (
    <Stack>
      <WalletQr child={child} />
      <Lines child={child} />
      <Cards child={child} />
    </Stack>
  );
}

// ---------------------------------------------------------------- the wallet QR

interface Qr {
  token: string;
  expiresAt: string;
}

function qrImage(token: string): string {
  const code = qrcode(0, "M");
  code.addData(token);
  code.make();
  return code.createDataURL(8, 4);
}

function WalletQr({ child }: { child: Child }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [qr, setQr] = useState<Qr | null>(null);
  const [expired, setExpired] = useState(false);
  const fetchQr = useAction(() => api.get<Qr>(`/wallets/${child.walletId}/qr`, { callerPersonId: personId }));

  useEffect(() => {
    if (!qr) return;
    const wait = new Date(qr.expiresAt).getTime() - Date.now();
    const timer = setTimeout(() => setExpired(true), Math.max(0, wait));
    return () => clearTimeout(timer);
  }, [qr]);

  async function get() {
    const r = await fetchQr.run();
    if (r.ok) {
      setQr(r.value);
      setExpired(false);
    }
  }

  return (
    <Card title={t("qrHeading")}>
      <p style={mutedStyle}>{t("qrHelp")}</p>
      {qr && !expired && (
        <>
          <img
            src={qrImage(qr.token)} alt={t("qrAlt", { name: child.name })}
            style={{ width: "min(100%, 24rem)", aspectRatio: "1", justifySelf: "center", imageRendering: "pixelated", background: "#ffffff", borderRadius: "var(--radius)" }}
          />
          <p style={{ ...mutedStyle, textAlign: "center" }}>{t("qrUntil", { time: formatDateTime(qr.expiresAt).slice(11) })}</p>
        </>
      )}
      {qr && expired && <Notice kind="warning">{t("qrExpired")}</Notice>}
      {fetchQr.error && <Notice kind="danger">{fetchQr.error.message}</Notice>}
      <Button disabled={fetchQr.busy} onClick={() => void get()}>{qr ? t("qrNew") : t("qrShow")}</Button>
    </Card>
  );
}

// ---------------------------------------------------------------- the statement

const DAYS = ["7", "30", "90"] as const;

function isoDay(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function Lines({ child }: { child: Child }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [days, setDays] = useState<string>("30");
  const range = () => ({ callerPersonId: personId, from: isoDay(new Date(Date.now() - Number(days) * 86_400_000)), to: isoDay(new Date()) });
  const list = useLoad(() => api.get<StatementLine[]>(`/wallets/${child.walletId}/statement`, range()), [child.walletId, personId, days]);
  const download = useAction((format: string) => api.download(`/wallets/${child.walletId}/statement`, { ...range(), format }));

  const kindText = (kind: string) => {
    const key = `kind${kind}` as StatementKey;
    return key in messages.en ? t(key) : kind;
  };

  return (
    <Card title={t("heading")}>
      <SelectField label={t("period")} value={days} onChange={(e) => setDays(e.target.value)}>
        {DAYS.map((d) => <option key={d} value={d}>{t(`days${d}`)}</option>)}
      </SelectField>
      {list.error && (
        <>
          <Notice kind="danger">{list.error.message}</Notice>
          <Button variant="secondary" onClick={list.reload}>{t("reload")}</Button>
        </>
      )}
      {!list.data && !list.error && <p>{t("loading")}</p>}
      {list.data && list.data.length === 0 && <p>{t("empty")}</p>}
      {list.data && list.data.length > 0 && (
        <ul style={listStyle}>
          {[...list.data].reverse().map((l, i) => (
            <li key={`${l.at}-${i}`} style={itemStyle}>
              <span style={mutedStyle}>{formatDateTime(l.at)}</span>
              <strong>{l.description}</strong>
              <span><Badge>{kindText(l.kind)}</Badge></span>
              <span style={{ fontSize: "var(--font-size-large)", fontWeight: 700 }}>{l.amountCents > 0 ? "+" : ""}{formatRand(l.amountCents)}</span>
              <span style={mutedStyle}>{t("balanceAfter", { amount: formatRand(l.balanceAfterCents) })}</span>
            </li>
          ))}
        </ul>
      )}
      {download.error && <Notice kind="danger">{download.error.message}</Notice>}
      <Button variant="secondary" disabled={download.busy} onClick={() => void download.run("csv").then((r) => { if (r.ok) saveFile(r.value); })}>{t("downloadCsv")}</Button>
      <Button variant="secondary" disabled={download.busy} onClick={() => void download.run("pdf").then((r) => { if (r.ok) saveFile(r.value); })}>{t("downloadPdf")}</Button>
    </Card>
  );
}

// ---------------------------------------------------------------- cards and wristbands

interface Issued extends Token {
  blockedHere: boolean;
}

function Cards({ child }: { child: Child }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [sure, setSure] = useState(false);
  const [note, setNote] = useState("");
  const [uid, setUid] = useState("");
  const [issued, setIssued] = useState<Issued[]>([]);

  const blockAll = useAction(() => api.post<number>(`/holders/${child.holderId}/block-cards`, undefined, { callerPersonId: personId }));
  const add = useAction(() => api.post<Token>(`/holders/${child.holderId}/tokens`, { callerPersonId: personId, type: "nfc", rawToken: uid.trim() }));
  const blockOne = useAction((id: string) => api.post(`/tokens/${id}/block`, undefined, { callerPersonId: personId }));

  return (
    <Card title={t("cardsHeading")}>
      <p style={mutedStyle}>{t("cardsHelp")}</p>
      {!sure && <Button onClick={() => { setSure(true); setNote(""); }}>{t("blockAll")}</Button>}
      {sure && (
        <Notice kind="warning">
          <Stack>
            <span>{t("blockSure", { name: child.name })}</span>
            <Button disabled={blockAll.busy} onClick={() => void blockAll.run().then((r) => { if (r.ok) { setSure(false); setNote(r.value > 0 ? t("blockedCount", { count: r.value }) : t("blockedNone")); } })}>{t("blockYes")}</Button>
            <Button variant="secondary" onClick={() => setSure(false)}>{t("blockNo")}</Button>
          </Stack>
        </Notice>
      )}
      {blockAll.error && <Notice kind="danger">{blockAll.error.message}</Notice>}
      {note && <Notice kind="success">{note}</Notice>}

      <strong>{t("addCard")}</strong>
      <form onSubmit={(e) => { e.preventDefault(); setNote(""); void add.run().then((r) => { if (r.ok) { setIssued((l) => [{ ...r.value, blockedHere: r.value.status !== "active" }, ...l]); setUid(""); setNote(t("cardAdded")); } }); }}>
        <Stack>
          <TextField label={t("cardNumber")} hint={t("cardHint")} value={uid} onChange={(e) => setUid(e.target.value)} autoComplete="off" />
          {add.error && <Notice kind="danger">{add.error.message}</Notice>}
          <Button type="submit" disabled={add.busy || uid.trim() === ""}>{t("addCardGo")}</Button>
        </Stack>
      </form>

      {issued.length > 0 && (
        <>
          <strong>{t("addedCards")}</strong>
          <ul style={listStyle}>
            {issued.map((c) => (
              <li key={c.id} style={itemStyle}>
                <span><Badge>{c.blockedHere ? t("cardBlocked") : t("cardActive")}</Badge></span>
                {!c.blockedHere && (
                  <Button variant="secondary" disabled={blockOne.busy} onClick={() => void blockOne.run(c.id).then((r) => { if (r.ok) { setIssued((l) => l.map((x) => (x.id === c.id ? { ...x, blockedHere: true } : x))); setNote(t("cardBlockedNow")); } })}>{t("blockOne")}</Button>
                )}
              </li>
            ))}
          </ul>
          {blockOne.error && <Notice kind="danger">{blockOne.error.message}</Notice>}
        </>
      )}
    </Card>
  );
}
