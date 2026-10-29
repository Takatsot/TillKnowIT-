import { useMessages } from "@tillknowit/app-kit";
import { Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { SupplierGate } from "../../shared/SupplierGate";
import { Tabs } from "../../shared/Tabs";
import { Deals } from "./Deals";
import { GroupDeals } from "./GroupDeals";
import { messages } from "./messages";

type Tab = "deals" | "group";

export function DealsArea() {
  const t = useMessages(messages);
  const [tab, setTab] = useState<Tab>("deals");
  const tabs = [{ id: "deals", label: t("tabDeals") }, { id: "group", label: t("tabGroup") }] as const;
  return (
    <SupplierGate>
      {(supplierId) => (
        <Stack>
          <Tabs label={t("tabs")} tabs={tabs} current={tab} onChange={setTab} />
          {tab === "deals" ? <Deals supplierId={supplierId} /> : <GroupDeals supplierId={supplierId} />}
        </Stack>
      )}
    </SupplierGate>
  );
}
