import { Button, Inline, Stack } from "@tillknowit/design-system";
import { useMessages } from "@tillknowit/app-kit";
import { useState } from "react";
import { useTillCaller } from "../selling-common/TillCaller";
import { Customers } from "./Customers";
import { Laybys } from "./Laybys";
import { Quotes } from "./Quotes";
import { Vouchers } from "./Vouchers";
import { messages } from "./messages";

type Tab = "customers" | "vouchers" | "quotes" | "layby";

export function CustomersLoyalty() {
  const t = useMessages(messages);
  const caller = useTillCaller();
  const [tab, setTab] = useState<Tab>("customers");
  const tabs: [Tab, string][] = [["customers", t("tabCustomers")], ["vouchers", t("tabVouchers")], ["quotes", t("tabQuotes")], ["layby", t("tabLayby")]];
  return (
    <Stack>
      {caller.card}
      <Inline>
        {tabs.map(([id, label]) => <Button key={id} variant={tab === id ? "primary" : "secondary"} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}
      </Inline>
      {tab === "customers" && <Customers caller={caller} />}
      {tab === "vouchers" && <Vouchers caller={caller} />}
      {tab === "quotes" && <Quotes caller={caller} />}
      {tab === "layby" && <Laybys caller={caller} />}
    </Stack>
  );
}
