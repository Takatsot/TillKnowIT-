import { formatDate, formatRand, parseRandToCents, useAction, useApi, useLoad, useMessages, useSession } from "@tillknowit/app-kit";
import { Badge, Button, Card, Inline, Notice, SelectField, Stack, Table, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { useProducts } from "../selling-common/products";
import type { TillCaller } from "../selling-common/TillCaller";
import { messages } from "./messages";

interface Layby {
  id: string; reference: string; customerName: string; customerPhone: string | null; totalCents: number; paidCents: number; balanceCents: number; dueOn: string;
  status: string; cancellationFeeCents: number; refundDueCents: number; payments: { amountCents: number; methodCode: string; paidAt: string }[];
}

export function Laybys({ caller }: { caller: TillCaller }) {
  const t = useMessages(messages);
  const api = useApi();
  const { shopId } = useSession();
  const products = useProducts();
  const till = { deviceId: caller.deviceId, staffId: caller.staffId };
  const list = useLoad(
    () => (caller.ready ? api.get<Layby[]>(`/shops/${shopId}/laybys`, till) : Promise.resolve({ ok: true as const, value: [] as Layby[] })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const held = useLoad(
    () => (caller.ready ? api.get<Record<string, number>>(`/shops/${shopId}/laybys/reservations`, till) : Promise.resolve({ ok: true as const, value: {} as Record<string, number> })),
    [shopId, caller.deviceId, caller.staffId, caller.ready],
  );
  const [note, setNote] = useState("");
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [methods, setMethods] = useState<Record<string, string>>({});
  const [sales, setSales] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState("");

  const act = useAction((id: string, what: "payments" | "collect" | "cancel", body: unknown) => api.post<Layby>(`/shops/${shopId}/laybys/${id}/${what}`, body));
  const reload = () => { list.reload(); held.reload(); };

  async function pay(l: Layby) {
    const cents = parseRandToCents(amounts[l.id] ?? "");
    if (cents === null || cents === 0) { setNote(""); setProblem(t("amountInvalid")); return; }
    setProblem(""); setNote("");
    const r = await act.run(l.id, "payments", { ...till, amountCents: cents, methodCode: methods[l.id] ?? "cash" });
    if (r.ok) { setNote(t("paid", { ref: l.reference, balance: formatRand(r.value.balanceCents) })); setAmounts((a) => ({ ...a, [l.id]: "" })); reload(); }
  }

  async function collect(l: Layby) {
    setProblem(""); setNote("");
    const r = await act.run(l.id, "collect", { ...till, saleId: (sales[l.id] ?? "").trim() });
    if (r.ok) { setNote(t("collected", { ref: l.reference })); reload(); }
  }

  async function cancel(l: Layby) {
    setProblem(""); setNote("");
    const r = await act.run(l.id, "cancel", till);
    if (r.ok) { setNote(t("laybyCancelled", { ref: l.reference, fee: formatRand(r.value.cancellationFeeCents), refund: formatRand(r.value.refundDueCents) })); reload(); }
  }

  const names = new Map((products.data ?? []).map((p) => [p.productId, p.name]));
  const reserved = Object.entries(held.data ?? {});

  return (
    <Stack>
      <Card title={t("laybyHeading")}>
        <Stack>
          <p>{t("laybyHint")}</p>
          {list.error && <Notice kind="danger">{list.error.message}</Notice>}
          {act.error && <Notice kind="danger">{act.error.message}</Notice>}
          {problem && <Notice kind="warning">{problem}</Notice>}
          {note && <Notice kind="success">{note}</Notice>}
          {list.loading && !list.data ? <p>{t("loading")}</p> : (
            <Table
              rows={list.data ?? []}
              rowKey={(l) => l.id}
              empty={t("laybyEmpty")}
              columns={[
                { header: t("colRef"), cell: (l) => l.reference },
                { header: t("colCustomer"), cell: (l) => l.customerName },
                { header: t("colTotal"), cell: (l) => formatRand(l.totalCents), align: "right" },
                { header: t("colPaid"), cell: (l) => formatRand(l.paidCents), align: "right" },
                { header: t("colBalance"), cell: (l) => formatRand(l.balanceCents), align: "right" },
                { header: t("colDue"), cell: (l) => formatDate(l.dueOn) },
                { header: t("colStatus"), cell: (l) => <Badge>{l.status}</Badge> },
                {
                  header: t("colActions"),
                  cell: (l) => (l.status !== "active" && l.status !== "completed" ? null : (
                    <Stack>
                      {l.status === "active" && (
                        <Inline>
                          <TextField label={t("payAmount", { ref: l.reference })} inputMode="decimal" value={amounts[l.id] ?? ""} onChange={(e) => setAmounts((a) => ({ ...a, [l.id]: e.target.value }))} />
                          <SelectField label={t("payMethod", { ref: l.reference })} value={methods[l.id] ?? "cash"} onChange={(e) => setMethods((m) => ({ ...m, [l.id]: e.target.value }))}>
                            <option value="cash">{t("cash")}</option>
                            <option value="card">{t("card")}</option>
                            <option value="qr">{t("qr")}</option>
                          </SelectField>
                          <Button disabled={act.busy} onClick={() => void pay(l)}>{t("pay", { ref: l.reference })}</Button>
                        </Inline>
                      )}
                      {l.status === "completed" && (
                        <Inline>
                          <TextField label={t("collectSale", { ref: l.reference })} value={sales[l.id] ?? ""} onChange={(e) => setSales((s) => ({ ...s, [l.id]: e.target.value }))} />
                          <Button disabled={act.busy || (sales[l.id] ?? "").trim() === ""} onClick={() => void collect(l)}>{t("collect", { ref: l.reference })}</Button>
                        </Inline>
                      )}
                      <div><Button variant="quiet" disabled={act.busy} onClick={() => void cancel(l)}>{t("cancelLayby", { ref: l.reference })}</Button></div>
                    </Stack>
                  )),
                },
              ]}
            />
          )}
        </Stack>
      </Card>
      <Card title={t("reservedHeading")}>
        {held.error && <Notice kind="danger">{held.error.message}</Notice>}
        <Table
          rows={reserved}
          rowKey={([id]) => id}
          empty={t("reservedEmpty")}
          columns={[
            { header: t("colProduct"), cell: ([id]) => names.get(id) ?? id },
            { header: t("colHeld"), cell: ([, n]) => n, align: "right" },
          ]}
        />
      </Card>
    </Stack>
  );
}
