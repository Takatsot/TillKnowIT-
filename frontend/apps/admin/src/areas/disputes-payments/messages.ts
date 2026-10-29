import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Disputes and payments",
  disputesHeading: "Open disputes",
  loading: "Loading...",
  empty: "There are no open disputes.",
  colRaised: "Raised",
  colBy: "Raised by",
  colOrder: "Order",
  colReason: "Reason",
  colStatus: "Status",
  colActions: "Actions",
  resolve: "Resolve",
  resolving: "Resolve the dispute on order {order}",
  outcome: "Outcome",
  pay_supplier: "Pay the supplier",
  refund_shop: "Refund the shop",
  outcomeHint: "Paying the supplier releases the held money. Refunding the shop does not release any of it.",
  note: "Note (optional)",
  confirm: "Resolve dispute",
  close: "Close",
  resolved: "The dispute on order {order} was resolved.",
  reconcileHeading: "Payment reconciliation",
  reconcileIntro: "Compares the payment partner's records with ours for one day.",
  day: "Day",
  run: "Run reconciliation",
  running: "Running...",
  report: "Report for {day}",
  matched: "{count} payments matched.",
  noMismatches: "Nothing is out of step for this day.",
  mismatches: "{count} do not match.",
  colRef: "Partner reference",
  colKind: "Kind",
  colDetail: "Detail",
} as const;

export type DisputeKey = keyof typeof en;
export const messages: AreaMessages<DisputeKey> = { en };
