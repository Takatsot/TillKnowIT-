import { useMessages } from "@tillknowit/app-kit";
import { Stack } from "@tillknowit/design-system";
import { useState } from "react";
import type { PostForm } from "../../shared/postForm";
import { SupplierGate } from "../../shared/SupplierGate";
import { Tabs } from "../../shared/Tabs";
import { ImportCatalogue } from "./ImportCatalogue";
import { messages } from "./messages";
import { Products } from "./Products";
import { Areas, Reps, Terms } from "./Settings";

type Tab = "products" | "import" | "areas" | "terms" | "reps";

export function Catalogue({ upload }: { upload?: PostForm } = {}) {
  const t = useMessages(messages);
  const [tab, setTab] = useState<Tab>("products");
  const tabs = [
    { id: "products", label: t("tabProducts") }, { id: "import", label: t("tabImport") }, { id: "areas", label: t("tabAreas") },
    { id: "terms", label: t("tabTerms") }, { id: "reps", label: t("tabReps") },
  ] as const;

  return (
    <SupplierGate>
      {(supplierId) => (
        <Stack>
          <Tabs label={t("tabs")} tabs={tabs} current={tab} onChange={setTab} />
          {tab === "products" && <Products supplierId={supplierId} />}
          {tab === "import" && <ImportCatalogue supplierId={supplierId} upload={upload} />}
          {tab === "areas" && <Areas supplierId={supplierId} />}
          {tab === "terms" && <Terms supplierId={supplierId} />}
          {tab === "reps" && <Reps supplierId={supplierId} />}
        </Stack>
      )}
    </SupplierGate>
  );
}
