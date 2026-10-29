import { formatDate, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";

interface Campaign { id: string; name: string; status: string; budgetCents: number; spentCents: number; areas: string[]; startsOn: string; endsOn: string; sponsored: boolean }
interface Usage { campaignId: string; showings: number; spentCents: number; remainingCents: number }

function NewCampaign({ onCreated }: { onCreated: (name: string) => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, supplierOrganisationId } = useSession();
  const [name, setName] = useState("");
  const [offer, setOffer] = useState("");
  const [budget, setBudget] = useState("");
  const [showing, setShowing] = useState("");
  const [areas, setAreas] = useState("");
  const [starts, setStarts] = useState("");
  const [ends, setEnds] = useState("");
  const [problem, setProblem] = useState("");
  const create = useAction((body: object) => api.post<Campaign>("/sponsored-campaigns", body));

  async function submit() {
    const b = parseRandToCents(budget);
    const s = parseRandToCents(showing);
    const codes = areas.split(",").map((a) => a.trim()).filter(Boolean);
    if (b === null || s === null) { setProblem(t("badMoney")); return; }
    if (codes.length === 0) { setProblem(t("needAreas")); return; }
    if (starts === "" || ends === "") { setProblem(t("needDates")); return; }
    setProblem("");
    const r = await create.run({
      personId, supplierOrganisationId, name: name.trim(), offerId: offer.trim() === "" ? null : offer.trim(), budgetCents: b, showingCents: s, areas: codes, startsOn: starts, endsOn: ends,
    });
    if (r.ok) { onCreated(r.value.name); setName(""); }
  }

  return (
    <Card title={t("newCampaign")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("fName")} value={name} onChange={(e) => setName(e.target.value)} required />
            <TextField label={t("fOffer")} value={offer} onChange={(e) => setOffer(e.target.value)} />
          </Inline>
          <Inline>
            <TextField label={t("fBudget")} inputMode="decimal" value={budget} onChange={(e) => setBudget(e.target.value)} />
            <TextField label={t("fShowing")} inputMode="decimal" value={showing} onChange={(e) => setShowing(e.target.value)} />
            <TextField label={t("fAreas")} value={areas} onChange={(e) => setAreas(e.target.value)} />
          </Inline>
          <Inline>
            <TextField label={t("fStarts")} type="date" value={starts} onChange={(e) => setStarts(e.target.value)} />
            <TextField label={t("fEnds")} type="date" value={ends} onChange={(e) => setEnds(e.target.value)} />
            <Button type="submit" disabled={create.busy || name.trim() === ""}>{t("create")}</Button>
          </Inline>
          {problem && <Notice kind="danger">{problem}</Notice>}
          {create.error && <Notice kind={create.error.code === "plan.feature_not_included" ? "warning" : "danger"}>{create.error.code === "plan.feature_not_included" ? t("planNeeded") : create.error.message}</Notice>}
        </Stack>
      </form>
    </Card>
  );
}

export function Campaigns() {
  const t = useMessages(messages);
  const api = useApi();
  const { personId, supplierOrganisationId } = useSession();
  const list = useLoad(() => api.get<Campaign[]>(`/suppliers/${supplierOrganisationId}/sponsored-campaigns`, { personId }), [supplierOrganisationId, personId]);
  const [note, setNote] = useState("");
  const [usage, setUsage] = useState<{ name: string; usage: Usage } | null>(null);
  const act = useAction((c: Campaign, pause: boolean) => api.post<Campaign>(`/sponsored-campaigns/${c.id}/pause`, undefined, { personId, paused: pause }));
  const look = useAction((c: Campaign) => api.get<Usage>(`/sponsored-campaigns/${c.id}/usage`, { personId }));

  async function toggle(c: Campaign) {
    const pause = c.status !== "paused";
    const r = await act.run(c, pause);
    if (r.ok) { setNote(t(pause ? "paused" : "resumed", { name: c.name })); list.reload(); }
  }

  async function showUsage(c: Campaign) {
    const r = await look.run(c);
    if (r.ok) setUsage({ name: c.name, usage: r.value });
  }

  return (
    <Stack>
      <Notice kind="info">{t("premiumNote")}</Notice>
      {note && <Notice kind="success">{note}</Notice>}
      {act.error && <Notice kind="danger">{act.error.message}</Notice>}
      {look.error && <Notice kind="danger">{look.error.message}</Notice>}
      {usage && <Notice kind="info">{t("usage", { name: usage.name })}: {t("usageLine", { showings: usage.usage.showings, spent: formatRand(usage.usage.spentCents), remaining: formatRand(usage.usage.remainingCents) })}</Notice>}
      <Card title={t("campaignsHeading")}>
        {list.error && <Notice kind="danger">{list.error.message}</Notice>}
        {list.loading && !list.data ? <p>{t("loading")}</p> : (
          <Table
            rows={list.data ?? []}
            rowKey={(c) => c.id}
            empty={t("campaignsEmpty")}
            columns={[
              { header: t("colName"), cell: (c) => <>{c.name} {c.sponsored && <Badge>{t("sponsored")}</Badge>}</> },
              { header: t("colStatus"), cell: (c) => <Badge>{c.status}</Badge> },
              { header: t("colBudget"), cell: (c) => formatRand(c.budgetCents), align: "right" },
              { header: t("colSpent"), cell: (c) => formatRand(c.spentCents), align: "right" },
              { header: t("colAreas"), cell: (c) => c.areas.join(", ") },
              { header: t("colRuns"), cell: (c) => `${formatDate(c.startsOn)} - ${formatDate(c.endsOn)}` },
              {
                header: "",
                cell: (c) => (
                  <Inline>
                    <Button variant="secondary" disabled={act.busy} aria-label={t(c.status === "paused" ? "resume" : "pause", { name: c.name })} onClick={() => void toggle(c)}>{t(c.status === "paused" ? "resumeShort" : "pauseShort")}</Button>
                    <Button variant="quiet" disabled={look.busy} aria-label={t("usage", { name: c.name })} onClick={() => void showUsage(c)}>{t("usageShort")}</Button>
                  </Inline>
                ),
              },
            ]}
          />
        )}
      </Card>
      <NewCampaign onCreated={(name) => { setNote(t("created", { name })); list.reload(); }} />
    </Stack>
  );
}
