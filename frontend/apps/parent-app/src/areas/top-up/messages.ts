import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Add money",
  balance: "Balance now",
  addHeading: "Add money",
  amount: "Amount in rand",
  amountHint: "From R20 to R5000.",
  amountBad: "Type an amount like 50 or 50,00.",
  start: "Add money",
  pending: "Waiting for payment",
  completed: "Money added",
  failed: "Payment failed",
  confirmed: "Money arrived",
  pendingHelp: "The money is added only after the payment partner confirms your payment.",
  pay: "Go to the payment page",
  check: "Check my balance",
  devNote: "Test mode: the payment partner is a pretend one. Its payment page does not open and no real money moves.",
  recent: "Added in this visit",
  topUpLine: "{amount} for {name}",
  transferHeading: "Move money between my children",
  transferFrom: "From {name}",
  to: "Move to",
  transferAmount: "Amount to move in rand",
  transfer: "Move money",
  transferred: "{amount} was moved from {from} to {to}.",
  needTwo: "You need two children linked to move money between them.",
  reference: "Moving money again is safe: the same request is never done twice.",
} as const;

export type TopUpKey = keyof typeof en;
export const messages: AreaMessages<TopUpKey> = { en };
