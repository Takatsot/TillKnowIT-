import { formatRand, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, CheckField, Notice, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { bigStyle, itemStyle, listStyle, mutedStyle } from "../../shared/ChildScope";
import type { Child } from "../../shared/types";
import { CONSENT_VERSION, messages } from "./messages";

export function Children() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const list = useLoad(() => api.get<Child[]>("/me/children", { callerPersonId: personId }), [personId]);
  const [code, setCode] = useState("");
  const [agreed, setAgreed] = useState(false);
  const [added, setAdded] = useState("");

  const link = useAction(() => api.post<Child>("/guardianships", { parentPersonId: personId, linkCode: code.trim(), consent: agreed, consentVersion: CONSENT_VERSION }));

  async function submit() {
    setAdded("");
    const r = await link.run();
    if (r.ok) {
      setAdded(t("linked", { name: r.value.name }));
      setCode("");
      setAgreed(false);
      list.reload();
    }
  }

  return (
    <Stack>
      <Card title={t("heading")}>
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
            {list.data.map((c) => (
              <li key={c.holderId} style={itemStyle}>
                <strong>{c.name}</strong>
                <span style={mutedStyle}>{c.school}{c.grade ? ` · ${t("classLine", { grade: c.grade, className: c.className ?? "" })}` : ""}</span>
                <span style={mutedStyle}>{t("balance")}</span>
                <p style={bigStyle}>{formatRand(c.balanceCents)}</p>
                <span>{c.dailyLimitCents === null ? t("noLimit") : `${t("dailyLimit")}: ${formatRand(c.dailyLimitCents)}`}</span>
                {c.walletStatus !== "active" && <Badge>{t("frozen")}</Badge>}
              </li>
            ))}
          </ul>
        )}
      </Card>
      <Card title={t("schoolDoesHeading")}>
        <p style={mutedStyle}>{t("schoolDoes")}</p>
      </Card>
      <Card title={t("add")}>
        <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          <Stack>
            <p style={mutedStyle}>{t("addHelp")}</p>
            <TextField label={t("code")} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="off" autoCapitalize="characters" required />
            <CheckField label={t("consent")} checked={agreed} onChange={(e) => setAgreed(e.target.checked)} />
            {link.error && <Notice kind="danger">{link.error.message}</Notice>}
            {added && <Notice kind="success">{added}</Notice>}
            <Button type="submit" disabled={link.busy || code.trim() === "" || !agreed}>{t("link")}</Button>
          </Stack>
        </form>
      </Card>
    </Stack>
  );
}
