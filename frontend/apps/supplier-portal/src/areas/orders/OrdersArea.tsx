import { useMessages } from "@tillknowit/app-kit";
import { Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { SupplierGate } from "../../shared/SupplierGate";
import { Tabs } from "../../shared/Tabs";
import { messages } from "./messages";
import { Orders } from "./Orders";
import { DemandView, Ratings, Statement } from "./Reports";

type Tab = "orders" | "statement" | "ratings" | "demand";
type Save = (file: { blob: Blob; fileName: string }) => void;

export function OrdersArea({ save }: { save?: Save } = {}) {
  const t = useMessages(messages);
  const [tab, setTab] = useState<Tab>("orders");
  const tabs = [{ id: "orders", label: t("tabOrders") }, { id: "statement", label: t("tabStatement") }, { id: "ratings", label: t("tabRatings") }, { id: "demand", label: t("tabDemand") }] as const;
  return (
    <SupplierGate>
      {(supplierId) => (
        <Stack>
          <Tabs label={t("tabs")} tabs={tabs} current={tab} onChange={setTab} />
          {tab === "orders" && <Orders supplierId={supplierId} />}
          {tab === "statement" && <Statement supplierId={supplierId} save={save} />}
          {tab === "ratings" && <Ratings supplierId={supplierId} />}
          {tab === "demand" && <DemandView supplierId={supplierId} />}
        </Stack>
      )}
    </SupplierGate>
  );
}
