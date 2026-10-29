import { useMessages } from "@tillknowit/app-kit";
import { Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { Tabs } from "../../shared/Tabs";
import { Campaigns } from "./Campaigns";
import { messages } from "./messages";
import { Plan } from "./Plan";

type Tab = "plan" | "campaigns";

/** Billing uses the supplier organisation id from the session, not the marketplace supplier id, so there is no supplier gate here. */
export function BillingCampaigns() {
  const t = useMessages(messages);
  const [tab, setTab] = useState<Tab>("plan");
  const tabs = [{ id: "plan", label: t("tabPlan") }, { id: "campaigns", label: t("tabCampaigns") }] as const;
  return (
    <Stack>
      <Tabs label={t("tabs")} tabs={tabs} current={tab} onChange={setTab} />
      {tab === "plan" ? <Plan /> : <Campaigns />}
    </Stack>
  );
}
