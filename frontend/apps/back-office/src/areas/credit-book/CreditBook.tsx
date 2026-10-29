import { formatDateTime, formatRand, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Notice, Stack, Table } from "@tillknowit/design-system";
import { useState } from "react";
import { messages, type CreditBookKey } from "./messages";

export interface CreditAccount {
  id: string;
  name: string;
  phone: string | null;
  creditLimitCents: number | null;
  balanceCents: number;
  overLimit: boolean;
}

export interface CreditEntry {
  id: string;
  type: string;
  amountCents: number;
  saleId: string | null;
  paymentMethodCode: string | null;
  createdAt: string;
}

const typeKeys: Record<string, CreditBookKey> = { sale: "type_sale", payment: "type_payment", cancel: "type_cancel" };

export function CreditBook() {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const accounts = useLoad(() => api.get<CreditAccount[]>(`/shops/${shopId}/credit-accounts`), [shopId]);
  const [open, setOpen] = useState<CreditAccount | null>(null);
  const entries = useLoad(
    () => (open ? api.get<CreditEntry[]>(`/shops/${shopId}/credit-accounts/${open.id}/entries`) : Promise.resolve({ ok: true as const, value: [] as CreditEntry[] })),
    [shopId, open?.id],
  );

  const rows = accounts.data ?? [];
  const over = rows.filter((a) => a.overLimit).length;
  const total = rows.reduce((sum, a) => sum + a.balanceCents, 0);

  return (
    <Stack>
      <Card title={t("heading")}>
        <Stack>
          {accounts.error && <Notice kind="danger">{accounts.error.message}</Notice>}
          {over > 0 && <Notice kind="warning">{t("overLimitWarning", { n: over })}</Notice>}
          {accounts.loading && !accounts.data ? <p>{t("loading")}</p> : (
            <>
              <Table
                rows={rows}
                rowKey={(a) => a.id}
                empty={t("empty")}
                columns={[
                  { header: t("colName"), cell: (a) => a.name },
                  { header: t("colPhone"), cell: (a) => a.phone ?? "" },
                  { header: t("colLimit"), align: "right", cell: (a) => (a.creditLimitCents === null ? t("noLimit") : formatRand(a.creditLimitCents)) },
                  { header: t("colBalance"), align: "right", cell: (a) => formatRand(a.balanceCents) },
                  { header: t("colStatus"), cell: (a) => <Badge>{a.overLimit ? t("overLimit") : t("ok")}</Badge> },
                  { header: t("colActions"), cell: (a) => <Button variant="secondary" aria-label={t("viewEntriesFor", { name: a.name })} onClick={() => setOpen(a)}>{t("viewEntries")}</Button> },
                ]}
              />
              {rows.length > 0 && <p>{t("totalOwed", { total: formatRand(total) })}</p>}
            </>
          )}
        </Stack>
      </Card>
      {open && (
        <Card title={t("entriesHeading", { name: open.name })}>
          <Stack>
            {entries.error && <Notice kind="danger">{entries.error.message}</Notice>}
            {entries.loading && !entries.data ? <p>{t("loading")}</p> : (
              <Table
                rows={entries.data ?? []}
                rowKey={(e) => e.id}
                empty={t("entriesEmpty")}
                columns={[
                  { header: t("colDate"), cell: (e) => formatDateTime(e.createdAt) },
                  { header: t("colType"), cell: (e) => (typeKeys[e.type] ? t(typeKeys[e.type]!) : e.type) },
                  { header: t("colAmount"), align: "right", cell: (e) => formatRand(e.amountCents) },
                  { header: t("colMethod"), cell: (e) => e.paymentMethodCode ?? "" },
                ]}
              />
            )}
            <Button variant="quiet" onClick={() => setOpen(null)}>{t("close")}</Button>
          </Stack>
        </Card>
      )}
    </Stack>
  );
}
