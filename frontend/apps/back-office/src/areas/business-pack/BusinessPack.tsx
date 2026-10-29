import { useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type BusinessPackKey } from "./messages";

export interface Feature { flag: string; settingsJson: string | null }
export interface Pack { id: string; code: string; name: string; description: string | null; slipTemplateCode: string; version: number; isActive: boolean; features: Feature[] }
export interface ShopPack { shopId: string; packCode: string; packName: string; packVersion: number; slipTemplateCode: string; features: Feature[] }

export function BusinessPack() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, shopId } = useSession();
  const packs = useLoad(() => api.get<Pack[]>("/business-packs"), []);
  const current = useLoad(() => api.get<ShopPack>(`/shops/${shopId}/business-pack`, { callerPersonId: personId }), [shopId, personId]);
  const [done, setDone] = useState("");
  const choose = useAction((pack: Pack) => api.put<ShopPack>(`/shops/${shopId}/business-pack`, { callerPersonId: personId, packCode: pack.code }));

  /** A flag this screen has no words for yet is shown as the flag itself: the server may be newer than we are. */
  const feature = (flag: string) => {
    const key = `feature_${flag}`;
    return Object.hasOwn(messages.en, key) ? t(key as BusinessPackKey) : flag;
  };

  async function pick(pack: Pack) {
    setDone("");
    const r = await choose.run(pack);
    if (r.ok) { setDone(t("chosen", { name: pack.name })); current.reload(); }
  }

  const loading = (packs.loading && !packs.data) || (current.loading && !current.data);

  return (
    <Stack>
      {current.error && <Notice kind="danger">{current.error.message}</Notice>}
      {packs.error && <Notice kind="danger">{packs.error.message}</Notice>}
      {current.data && (
        <Card title={t("currentHeading")}>
          <p><strong>{current.data.packName}</strong> <Badge>{t("currentVersion", { version: current.data.packVersion })}</Badge></p>
          {current.data.features.length === 0 ? <p>{t("currentNone")}</p> : (
            <ul>{current.data.features.map((f) => <li key={f.flag}>{feature(f.flag)}</li>)}</ul>
          )}
        </Card>
      )}
      <Card title={t("packsHeading")}>
        <Stack>
          <p>{t("packsHint")}</p>
          {loading && <p>{t("loading")}</p>}
          {choose.error && <Notice kind="danger">{choose.error.message}</Notice>}
          {done && <Notice kind="success">{done}</Notice>}
          {packs.data && packs.data.length === 0 && <p>{t("empty")}</p>}
          {(packs.data ?? []).map((p) => {
            const isCurrent = current.data?.packCode === p.code;
            return (
              <Card key={p.code} title={p.name}>
                <Stack>
                  {p.description && <p>{p.description}</p>}
                  <p>{t("features")}</p>
                  {p.features.length === 0 ? <p>{t("noFeatures")}</p> : <ul>{p.features.map((f) => <li key={f.flag}>{feature(f.flag)}</li>)}</ul>}
                  <Inline>
                    {isCurrent && <Badge>{t("chosenBadge")}</Badge>}
                    <Button disabled={isCurrent || choose.busy} onClick={() => void pick(p)}>{t("choose", { name: p.name })}</Button>
                  </Inline>
                </Stack>
              </Card>
            );
          })}
        </Stack>
      </Card>
    </Stack>
  );
}
