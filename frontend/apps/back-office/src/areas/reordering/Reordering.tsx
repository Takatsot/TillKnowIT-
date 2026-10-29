import { useMessages } from "@tillknowit/app-kit";
import { Button, Inline, Stack } from "@tillknowit/design-system";
import { useState } from "react";
import { GroupDeals } from "./GroupDeals";
import { OrderList } from "./OrderList";
import { Orders } from "./Orders";
import { Settings } from "./Settings";
import { messages } from "./messages";

type Tab = "list" | "orders" | "groups" | "settings";

export function Reordering() {
  const t = useMessages(messages);
  const [tab, setTab] = useState<Tab>("list");
  const [ordersKey, setOrdersKey] = useState(0);
  const tabs: [Tab, ReturnType<typeof t>][] = [["list", t("tabList")], ["orders", t("tabOrders")], ["groups", t("tabGroups")], ["settings", t("tabSettings")]];

  return (
    <Stack>
      <Inline>
        {tabs.map(([id, label]) => (
          <Button key={id} variant={tab === id ? "primary" : "secondary"} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>
        ))}
      </Inline>
      {tab === "list" && <OrderList onBuilt={() => setOrdersKey((n) => n + 1)} />}
      {tab === "orders" && <Orders refreshKey={ordersKey} />}
      {tab === "groups" && <GroupDeals />}
      {tab === "settings" && <Settings />}
    </Stack>
  );
}
