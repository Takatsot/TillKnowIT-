import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Cash-up",
  intro: "Count the cash in the drawer and compare it with what the till expects for {day}.",
  float: "Float (cash the drawer started the day with)",
  counted: "Cash counted in the drawer",
  note: "Note (optional)",
  "col.what": "What",
  "col.amount": "Amount",
  "row.float": "Float",
  "row.sales": "Cash sales",
  "row.credit": "Paid on credit accounts in cash",
  "row.refunds": "Cash given back in refunds",
  "row.expected": "Expected in the drawer",
  short: "Short by {amount}",
  over: "Over by {amount}",
  balanced: "The drawer balances.",
  save: "Save cash-up",
  saved: "Cash-up saved for {day}. {result}",
  already: "A cash-up for {day} was already saved from this till. The server keeps one per day, so a second one may be refused.",
  invalid: "Type the float and the cash counted as amounts, for example 500 or 1234,50.",
  saveFailed: "That could not be saved: {message}",
} as const;

export type CashUpKey = keyof typeof en;

export const messages: AreaMessages<CashUpKey> = { en };
