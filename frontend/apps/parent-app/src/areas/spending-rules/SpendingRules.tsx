import { formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, CheckField, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { ChildScope, itemStyle, listStyle, mutedStyle } from "../../shared/ChildScope";
import type { Child, Menu, Restriction, Wallet } from "../../shared/types";
import { messages } from "./messages";

export function SpendingRules() {
  return <ChildScope render={({ child }) => <Rules key={child.walletId} child={child} />} />;
}

function Rules({ child }: { child: Child }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const wallet = useLoad(() => api.get<Wallet>(`/wallets/${child.walletId}`, { callerPersonId: personId }), [child.walletId, personId]);

  if (wallet.error) {
    return (
      <Stack>
        <Notice kind="danger">{wallet.error.message}</Notice>
        <Button variant="secondary" onClick={wallet.reload}>{t("reload")}</Button>
      </Stack>
    );
  }
  if (!wallet.data) return <p>{t("loading")}</p>;
  return (
    <Stack>
      <Limits child={child} wallet={wallet.data} onSaved={wallet.reload} />
      <Blocks child={child} wallet={wallet.data} onSaved={wallet.reload} />
    </Stack>
  );
}

const centsText = (cents: number | null) => (cents === null ? "" : String(cents / 100).replace(".", ","));

function Limits({ child, wallet, onSaved }: { child: Child; wallet: Wallet; onSaved: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [limit, setLimit] = useState(centsText(wallet.dailyLimitCents));
  const [notify, setNotify] = useState(wallet.notifyOnPurchase);
  const [low, setLow] = useState(centsText(wallet.lowBalanceAlertCents));
  const [saved, setSaved] = useState("");

  const limitCents = limit.trim() === "" ? null : parseRandToCents(limit);
  const lowCents = low.trim() === "" ? null : parseRandToCents(low);
  const limitBad = limit.trim() !== "" && limitCents === null;
  const lowBad = low.trim() !== "" && lowCents === null;

  const save = useAction(() => api.put<Wallet>(`/wallets/${wallet.id}/limits`, {
    callerPersonId: personId, dailyLimitCents: limitCents, notifyOnPurchase: notify, lowBalanceAlertCents: lowCents, clearLowBalanceAlert: lowCents === null,
  }));

  return (
    <Card title={t("limitHeading")}>
      <p style={mutedStyle}>{t("limitMeans", { name: child.name })}</p>
      <p>{t("spentToday", { amount: formatRand(wallet.spentTodayCents) })}</p>
      <form onSubmit={(e) => { e.preventDefault(); setSaved(""); void save.run().then((r) => { if (r.ok) { setSaved(t("limitsSaved")); onSaved(); } }); }}>
        <Stack>
          <TextField label={t("limit")} hint={t("limitHint")} value={limit} onChange={(e) => setLimit(e.target.value)} inputMode="decimal" error={limitBad ? t("amountBad") : null} />
          <CheckField label={t("notify", { name: child.name })} checked={notify} onChange={(e) => setNotify(e.target.checked)} />
          <TextField label={t("lowAlert")} hint={t("lowAlertHint")} value={low} onChange={(e) => setLow(e.target.value)} inputMode="decimal" error={lowBad ? t("amountBad") : null} />
          {save.error && <Notice kind="danger">{save.error.message}</Notice>}
          {saved && <Notice kind="success">{saved}</Notice>}
          <Button type="submit" disabled={save.busy || limitBad || lowBad}>{t("saveLimits")}</Button>
        </Stack>
      </form>
    </Card>
  );
}

function Blocks({ child, wallet, onSaved }: { child: Child; wallet: Wallet; onSaved: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [kind, setKind] = useState("category");
  const [key, setKey] = useState("");
  const [saved, setSaved] = useState("");
  // The menu only helps to pick a name; if it cannot be loaded the parent can still type one.
  const menu = useLoad(() => api.get<Menu>(`/holders/${child.holderId}/menu`, { callerPersonId: personId }), [child.holderId, personId]);

  const replace = useAction((list: Restriction[]) => api.put<Wallet>(`/wallets/${wallet.id}/restrictions`, { callerPersonId: personId, restrictions: list }));

  async function change(list: Restriction[], clearKey: boolean) {
    setSaved("");
    const r = await replace.run(list);
    if (r.ok) {
      setSaved(t("blocked"));
      if (clearKey) setKey("");
      onSaved();
    }
  }

  const have = (k: string, v: string) => wallet.restrictions.some((r) => r.kind === k && r.key === v.trim().toLowerCase());
  const suggestions = [
    ...[...new Set((menu.data?.items ?? []).map((i) => i.categoryCode).filter((c): c is string => !!c))].map((c) => ({ kind: "category", key: c })),
    ...(menu.data?.items ?? []).filter((i) => i.productId).map((i) => ({ kind: "product", key: i.productId!, label: i.name })),
  ].filter((s) => !have(s.kind, s.key));

  return (
    <Card title={t("blockHeading", { name: child.name })}>
      <p style={mutedStyle}>{t("blockMeans")}</p>
      {wallet.restrictions.length === 0 ? <p>{t("noBlocks")}</p> : (
        <ul style={listStyle}>
          {wallet.restrictions.map((r) => (
            <li key={`${r.kind}:${r.key}`} style={itemStyle}>
              <strong>{r.kind === "category" ? t("category") : t("product")}</strong>
              <span>{r.kind === "category" ? t("categoryMeans", { key: r.key }) : t("productMeans", { key: r.key })}</span>
              <Button variant="secondary" disabled={replace.busy} aria-label={t("removeLabel", { key: r.key })} onClick={() => void change(wallet.restrictions.filter((x) => x !== r), false)}>{t("remove")}</Button>
            </li>
          ))}
        </ul>
      )}
      <strong>{t("addHeading")}</strong>
      <form onSubmit={(e) => { e.preventDefault(); if (key.trim() !== "" && !have(kind, key)) void change([...wallet.restrictions, { kind, key: key.trim() }], true); }}>
        <Stack>
          <SelectField label={t("kind")} value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="category">{t("category")}</option>
            <option value="product">{t("product")}</option>
          </SelectField>
          <TextField label={t("key")} hint={t("keyHint")} value={key} onChange={(e) => setKey(e.target.value)} maxLength={100} />
          {suggestions.length > 0 && (
            <div>
              <span style={mutedStyle}>{t("suggest")}</span>
              <ul style={{ ...listStyle, gridTemplateColumns: "repeat(auto-fill, minmax(10rem, 1fr))" }}>
                {suggestions.map((s) => (
                  <li key={`${s.kind}:${s.key}`}>
                    <Button variant="quiet" onClick={() => { setKind(s.kind); setKey(s.key); }}>{"label" in s ? `${s.label}` : s.key}</Button>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {replace.error && <Notice kind="danger">{replace.error.message}</Notice>}
          {saved && <Notice kind="success">{saved}</Notice>}
          <Button type="submit" disabled={replace.busy || key.trim() === ""}>{t("block")}</Button>
        </Stack>
      </form>
    </Card>
  );
}
