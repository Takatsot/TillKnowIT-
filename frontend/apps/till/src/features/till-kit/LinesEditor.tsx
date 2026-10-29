import { formatRand, lineTotal, type CatalogueItem, type UnitDivisor } from "@tillknowit/domain";
import type { LanguageCode } from "@tillknowit/i18n";
import { Button, Inline, Notice, Stack, Table, TextField } from "@tillknowit/design-system";
import { useMemo, useState } from "react";
import { useKitT } from "./kit";
import type { TicketLineDto } from "./types";

export const divisorOf = (l: TicketLineDto): UnitDivisor => (l.unitDivisor === 1000 ? 1000 : 1);
export const lineCents = (l: TicketLineDto): number => lineTotal(l.priceCents, l.quantity, divisorOf(l));
export const linesTotal = (lines: readonly TicketLineDto[]): number => lines.reduce((sum, l) => sum + lineCents(l), 0);

/** The lines the server holds, as the till's catalogue items they came from (for the cost of a line when the till rings it up). */
export const costOf = (catalogue: readonly CatalogueItem[], productId: string): number => catalogue.find((c) => c.id === productId)?.costCents ?? 0;

interface Props {
  catalogue: readonly CatalogueItem[];
  language: LanguageCode;
  lines: readonly TicketLineDto[];
  onChange: (lines: TicketLineDto[]) => void;
  /** Lets the person write a note on each line (the kitchen reads it). */
  allowNotes?: boolean;
  /** Read only: shows the lines without the search and the change buttons. */
  readOnly?: boolean;
}

/**
 * Picks catalogue items into a list of priced lines. The prices come from the till's own catalogue; the server checks them again when the list is saved.
 * Used by every tab that builds a list of items on its own: tickets, quotes, lay-by, wallet payments.
 */
export function LinesEditor({ catalogue, language, lines, onChange, allowNotes = false, readOnly = false }: Props) {
  const t = useKitT(language);
  const [text, setText] = useState("");
  const [weighing, setWeighing] = useState<CatalogueItem | null>(null);
  const [grams, setGrams] = useState("");
  const [problem, setProblem] = useState("");

  const matches = useMemo(() => {
    const q = text.trim().toLowerCase();
    return q === "" ? [] : catalogue.filter((c) => c.name.toLowerCase().includes(q) || (c.barcode !== null && c.barcode === q)).slice(0, 8);
  }, [text, catalogue]);

  function add(item: CatalogueItem) {
    setText("");
    setProblem("");
    if (item.soldByWeight) return setWeighing(item);
    const at = lines.findIndex((l) => l.productId === item.id && divisorOf(l) === 1 && !l.note);
    if (at >= 0) onChange(lines.map((l, i) => (i === at ? { ...l, quantity: l.quantity + 1 } : l)));
    else onChange([...lines, { productId: item.id, name: item.name, quantity: 1, priceCents: item.priceCents, unitDivisor: 1 }]);
  }

  function addWeighed() {
    const g = Number(grams);
    if (!weighing || !Number.isInteger(g) || g <= 0 || g > 50_000) return setProblem(t("kit.weightLabel", { name: weighing?.name ?? "" }));
    onChange([...lines, { productId: weighing.id, name: weighing.name, quantity: g, priceCents: weighing.priceCents, unitDivisor: 1000 }]);
    setWeighing(null);
    setGrams("");
    setProblem("");
  }

  const change = (at: number, quantity: number) => onChange(quantity <= 0 ? lines.filter((_, i) => i !== at) : lines.map((l, i) => (i === at ? { ...l, quantity } : l)));

  return (
    <Stack>
      {readOnly ? null : (
        <>
          <TextField label={t("kit.search")} hint={t("kit.searchHint")} value={text} onChange={(e) => setText(e.target.value)} autoComplete="off" />
          {text.trim() !== "" && matches.length === 0 ? <Notice>{t("kit.noMatch")}</Notice> : null}
          {matches.length > 0 ? (
            <ul aria-label={t("kit.search")} style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-2)" }}>
              {matches.map((m) => (
                <li key={m.id}>
                  <Button variant="secondary" onClick={() => add(m)} aria-label={t("kit.add", { name: m.name })}>{m.name} {formatRand(m.priceCents)}</Button>
                </li>
              ))}
            </ul>
          ) : null}
          {weighing ? (
            <Inline>
              <TextField label={t("kit.weightLabel", { name: weighing.name })} inputMode="numeric" value={grams} onChange={(e) => setGrams(e.target.value)} />
              <Button onClick={addWeighed}>{t("kit.addWeighed")}</Button>
              <Button variant="quiet" onClick={() => setWeighing(null)}>{t("kit.cancel")}</Button>
            </Inline>
          ) : null}
          {problem ? <Notice kind="danger">{problem}</Notice> : null}
        </>
      )}

      <Table
        empty={t("kit.noLines")}
        rows={lines.map((l, i) => ({ l, i }))}
        rowKey={(r) => `${r.l.productId}-${r.i}`}
        columns={[
          {
            header: t("kit.colItem"),
            cell: ({ l, i }) => (
              <>
                {l.name}
                {allowNotes && !readOnly ? (
                  <TextField label={t("kit.note", { name: l.name })} value={l.note ?? ""} onChange={(e) => onChange(lines.map((x, j) => (j === i ? { ...x, note: e.target.value } : x)))} />
                ) : l.note ? (
                  <div>{l.note}</div>
                ) : null}
              </>
            ),
          },
          { header: t("kit.colQty"), cell: ({ l }) => (divisorOf(l) === 1000 ? t("kit.grams", { grams: l.quantity }) : String(l.quantity)), align: "right" },
          { header: t("kit.colPrice"), cell: ({ l }) => formatRand(l.priceCents), align: "right" },
          { header: t("kit.colTotal"), cell: ({ l }) => formatRand(lineCents(l)), align: "right" },
          ...(readOnly
            ? []
            : [
                {
                  header: t("kit.colActions"),
                  cell: ({ l, i }: { l: TicketLineDto; i: number }) => (
                    <Inline>
                      {divisorOf(l) === 1 ? (
                        <>
                          <Button variant="secondary" aria-label={t("kit.less", { name: l.name })} onClick={() => change(i, l.quantity - 1)}>-</Button>
                          <Button variant="secondary" aria-label={t("kit.more", { name: l.name })} onClick={() => change(i, l.quantity + 1)}>+</Button>
                        </>
                      ) : null}
                      <Button variant="quiet" aria-label={t("kit.remove", { name: l.name })} onClick={() => change(i, 0)}>x</Button>
                    </Inline>
                  ),
                },
              ]),
        ]}
      />
      <strong>{t("kit.total", { amount: formatRand(linesTotal(lines)) })}</strong>
    </Stack>
  );
}
