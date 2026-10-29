import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Credit book",
  heading: "Customers on credit",
  loading: "Loading...",
  empty: "No customers on credit yet.",
  colName: "Customer",
  colPhone: "Phone",
  colLimit: "Credit limit",
  colBalance: "Owes",
  colStatus: "Status",
  colActions: "Actions",
  noLimit: "No limit",
  overLimit: "Over limit",
  ok: "Within limit",
  overLimitWarning: "{n} customers are over their credit limit. This is only a warning: sales are never blocked.",
  totalOwed: "Total owed: {total}",
  viewEntries: "View entries",
  viewEntriesFor: "View entries for {name}",
  entriesHeading: "Entries for {name}",
  entriesEmpty: "No entries for this customer yet.",
  colDate: "Date",
  colType: "Type",
  colAmount: "Amount",
  colMethod: "Paid by",
  type_sale: "Credit sale",
  type_payment: "Payment",
  type_cancel: "Cancelled sale",
  close: "Close",
} as const;

export type CreditBookKey = keyof typeof en;
export const messages: AreaMessages<CreditBookKey> = { en };
