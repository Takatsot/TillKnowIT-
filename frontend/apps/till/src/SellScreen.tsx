import { Button } from "@tillknowit/design-system";
import {
  addItem, addWeighed, emptyBasket, formatRand, newId, queueSale, quickCashOptions, removeItem, scan, setQuantity, summariseBasket, toSaleUpload,
  type Basket, type CatalogueItem, type EmbeddedBarcodeRules, type Outbox, type Payment, type PromotionDef,
} from "@tillknowit/domain";
import { translate, type LanguageCode, type MessageKey } from "@tillknowit/i18n";
import { useMemo, useState } from "react";
import styles from "./SellScreen.module.css";
import type { StaffRecord } from "./staffStore";

interface Props {
  staff: StaffRecord;
  language: LanguageCode;
  catalogue: readonly CatalogueItem[];
  promotions: readonly PromotionDef[];
  /** Scale label rules, when the shop's pack uses them. */
  barcodeRules: EmbeddedBarcodeRules | null;
  outbox: Outbox;
  /** Called after a sale is safely in the queue, so the till can try to send it. */
  onSaleQueued?: () => void;
  /** Makes the time and the ids; tests pass their own. */
  clock?: () => Date;
}

type Step = "basket" | "pay" | "cash";

/**
 * The selling screen. A sale is built in the basket, paid, and written to the outbox before the screen says it is done: nothing here waits for a network.
 * All the rules (prices, promotions, VAT, payments) are in @tillknowit/domain, the same as the server's.
 */
export function SellScreen({ staff, language, catalogue, promotions, barcodeRules, outbox, onSaleQueued, clock = () => new Date() }: Props) {
  const t = (key: MessageKey, params?: Record<string, string | number>) => translate(language, key, params);
  const [basket, setBasket] = useState<Basket>(emptyBasket);
  const [step, setStep] = useState<Step>("basket");
  const [entry, setEntry] = useState("");
  const [weighing, setWeighing] = useState<CatalogueItem | null>(null);
  const [grams, setGrams] = useState("");
  const [cashGiven, setCashGiven] = useState("");
  const [message, setMessage] = useState("");
  const [saved, setSaved] = useState("");

  const summary = useMemo(() => summariseBasket(basket, promotions, clock()), [basket, promotions, clock]);

  const matches = useMemo(() => {
    const q = entry.trim().toLowerCase();
    if (q.length < 2 || /^\d+$/.test(q)) return [];
    return catalogue.filter((c) => c.name.toLowerCase().includes(q)).slice(0, 6);
  }, [entry, catalogue]);

  function problem(code: string): string {
    return code === "basket.unknown_item" ? t("sell.unknownCode") : code.startsWith("weight.") ? t("sell.error.weight") : t("sell.error.generic");
  }

  function pick(item: CatalogueItem) {
    setSaved("");
    setMessage("");
    setEntry("");
    if (item.soldByWeight) return setWeighing(item);
    const added = addItem(basket, item);
    if (added.ok) setBasket(added.value);
    else setMessage(problem(added.code));
  }

  function submitEntry() {
    const code = entry.trim();
    if (code === "") return;
    setSaved("");
    const outcome = scan(basket, code, catalogue, barcodeRules);
    if (outcome.ok) {
      setMessage("");
      setEntry("");
      if (outcome.value.kind === "weigh") setWeighing(outcome.value.item);
      else setBasket(outcome.value.basket);
    } else if (matches.length === 1 && matches[0]) pick(matches[0]);
    else if (matches.length === 0) setMessage(/^\d+$/.test(code) ? problem(outcome.code) : t("sell.noMatch"));
  }

  function submitWeight() {
    if (!weighing) return;
    const added = addWeighed(basket, weighing, Number(grams));
    if (added.ok) {
      setBasket(added.value);
      setWeighing(null);
      setGrams("");
      setMessage("");
    } else setMessage(problem(added.code));
  }

  async function finish(payments: Payment[]) {
    const now = clock();
    const sale = toSaleUpload(basket, summary, payments, { saleId: newId(now.getTime()), staffId: staff.id }, now);
    if (!sale.ok) return setMessage(problem(sale.code));
    await queueSale(outbox, sale.value, now);
    const change = sale.value.changeCents;
    setSaved(t("sell.saved", { amount: formatRand(summary.dueCents) }) + (change ? ` ${t("sell.change", { amount: formatRand(change) })}` : ""));
    setBasket(emptyBasket);
    setStep("basket");
    setCashGiven("");
    setMessage("");
    onSaleQueued?.();
  }

  const rand = (cents: number) => formatRand(cents);
  const given = Math.round(Number(cashGiven.replace(",", ".")) * 100);
  const cashReady = Number.isFinite(given) && given >= summary.dueCents;

  if (weighing) {
    return (
      <section className={styles.panel} aria-labelledby="weigh">
        <h1 id="weigh" className={styles.title}>{t("sell.weigh", { name: weighing.name })}</h1>
        <input className={styles.input} inputMode="numeric" aria-label={t("sell.weigh", { name: weighing.name })} value={grams} onChange={(e) => setGrams(e.target.value)} autoFocus />
        <p className={styles.message} role="status">{message}</p>
        <div className={styles.row}>
          <Button variant="quiet" onClick={() => { setWeighing(null); setGrams(""); }}>{t("sell.back")}</Button>
          <Button onClick={submitWeight} disabled={grams.trim() === ""}>{t("sell.weightAdd")}</Button>
        </div>
      </section>
    );
  }

  if (step === "pay" || step === "cash") {
    return (
      <section className={styles.panel} aria-labelledby="pay">
        <h1 id="pay" className={styles.title}>{t("sell.chooseMethod")}</h1>
        <p className={styles.due}>{t("sell.total")}: <strong>{rand(summary.dueCents)}</strong></p>
        {step === "pay" ? (
          <div className={styles.methods}>
            <Button onClick={() => setStep("cash")}>{t("sell.cash")}</Button>
            <Button variant="secondary" onClick={() => finish([{ method: "card", amountCents: summary.dueCents }])}>{t("sell.card")}</Button>
            <Button variant="secondary" onClick={() => finish([{ method: "qr", amountCents: summary.dueCents }])}>{t("sell.qr")}</Button>
          </div>
        ) : (
          <>
            <div className={styles.methods}>
              {quickCashOptions(summary.dueCents).map((c) => (
                <Button key={c} variant="secondary" onClick={() => finish([{ method: "cash", amountCents: summary.dueCents, tenderedCents: c }])}>
                  {c === summary.dueCents ? t("sell.exact") : rand(c)}
                </Button>
              ))}
            </div>
            <label className={styles.label}>
              {t("sell.cashGiven")}
              <input className={styles.input} inputMode="decimal" value={cashGiven} onChange={(e) => setCashGiven(e.target.value)} />
            </label>
            <Button disabled={!cashReady} onClick={() => finish([{ method: "cash", amountCents: summary.dueCents, tenderedCents: given }])}>
              {cashReady && given > summary.dueCents ? t("sell.change", { amount: rand(given - summary.dueCents) }) : t("sell.pay")}
            </Button>
          </>
        )}
        <p className={styles.message} role="status">{message}</p>
        <Button variant="quiet" onClick={() => setStep("basket")}>{t("sell.back")}</Button>
      </section>
    );
  }

  return (
    <section className={styles.panel} aria-label={t("sell.total")}>
      <form className={styles.row} onSubmit={(e) => { e.preventDefault(); submitEntry(); }}>
        <input className={styles.input} value={entry} placeholder={t("sell.scanOrSearch")} aria-label={t("sell.scanOrSearch")} onChange={(e) => setEntry(e.target.value)} autoFocus />
        <Button type="submit">{t("sell.add")}</Button>
      </form>

      {matches.length > 0 && (
        <ul className={styles.matches}>
          {matches.map((m) => <li key={m.id}><Button variant="secondary" className={styles.match} onClick={() => pick(m)}>{m.name} <span>{rand(m.priceCents)}{m.soldByWeight ? "/kg" : ""}</span></Button></li>)}
        </ul>
      )}

      <p className={styles.message} role="status" aria-live="polite">{message || saved}</p>

      {summary.lines.length === 0 ? <p className={styles.empty}>{t("sell.empty")}</p> : (
        <ul className={styles.lines}>
          {summary.lines.map((l) => (
            <li key={l.productId} className={styles.line}>
              <span className={styles.name}>{l.name}</span>
              <span className={styles.qty}>{l.unitDivisor === 1000 ? `${l.quantity} g` : `× ${l.quantity}`}</span>
              <span className={styles.amount}>{rand(l.totalCents)}</span>
              {l.unitDivisor === 1 && l.fixedTotalCents === undefined && (
                <span className={styles.controls}>
                  <Button variant="quiet" aria-label={`${t("sell.less")}: ${l.name}`} onClick={() => { const r = setQuantity(basket, l.productId, l.quantity - 1); if (r.ok) setBasket(r.value); }}>−</Button>
                  <Button variant="quiet" aria-label={`${t("sell.more")}: ${l.name}`} onClick={() => { const r = setQuantity(basket, l.productId, l.quantity + 1); if (r.ok) setBasket(r.value); else setMessage(problem(r.code)); }}>+</Button>
                </span>
              )}
              <Button variant="quiet" aria-label={`${t("sell.remove")}: ${l.name}`} onClick={() => setBasket(removeItem(basket, l.productId))}>✕</Button>
            </li>
          ))}
        </ul>
      )}

      <dl className={styles.totals}>
        {summary.discountCents > 0 && (<><dt>{t("sell.discount")}</dt><dd>−{rand(summary.discountCents)}</dd></>)}
        <dt>{t("sell.vat")}</dt><dd>{rand(summary.vatCents)}</dd>
        <dt className={styles.grand}>{t("sell.total")}</dt><dd className={styles.grand}>{rand(summary.totalCents)}</dd>
      </dl>

      <div className={styles.row}>
        <Button variant="quiet" disabled={basket.entries.length === 0} onClick={() => { setBasket(emptyBasket); setMessage(""); }}>{t("sell.cancelSale")}</Button>
        <Button disabled={basket.entries.length === 0} onClick={() => { setSaved(""); setStep("pay"); }}>{t("sell.pay")} {rand(summary.dueCents)}</Button>
      </div>
    </section>
  );
}
