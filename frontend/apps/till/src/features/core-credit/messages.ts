import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Credit book",
  find: "Find a customer by name or phone",
  empty: "There are no credit customers on this till yet. The owner adds them in the back office.",
  noMatch: "Nobody matches that.",
  offlineCopy: "The till is offline, so these are the balances from the last time it was online, plus what has been taken since.",
  "col.name": "Customer",
  "col.phone": "Phone",
  "col.owes": "Owes",
  "col.limit": "Limit",
  "col.choose": "Take payment",
  choose: "Take payment from {name}",
  back: "Back to the list",
  owes: "{name} owes {amount}.",
  paysNothing: "{name} owes nothing.",
  amount: "Amount paid",
  method: "Paid by",
  "method.cash": "Cash",
  "method.card": "Card",
  "method.qr": "QR",
  save: "Save payment",
  saved: "Payment of {amount} from {name} saved. They now owe {left}.",
  invalid: "Type the amount as a number, for example 50 or 25,50.",
  tooMuch: "That is more than {name} owes ({amount}).",
  saveFailed: "That could not be saved: {message}",
  unreachable: "The server could not be reached, so this is the copy kept on the till.",
} as const;

export type CreditKey = keyof typeof en;

export const messages: AreaMessages<CreditKey> = { en };
