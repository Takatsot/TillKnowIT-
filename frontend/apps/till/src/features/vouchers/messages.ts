import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Vouchers",
  "tab.sell": "Sell a voucher",
  "tab.check": "Check a balance",
  "tab.redeem": "Use a voucher",
  "sell.amount": "Voucher value",
  "sell.amountHint": "From R1 up to R5000.",
  "sell.paidWith": "Customer pays with",
  "sell.button": "Sell voucher",
  "sell.invalid": "Type the value in rand, for example 200 or 200,50.",
  "sell.method.cash": "Cash",
  "sell.method.card": "Card",
  "sell.method.qr": "QR",
  "sell.codeTitle": "Voucher code",
  "sell.codeWarning": "Give this code to the customer now. It is shown only once and cannot be looked up again.",
  "sell.codeValue": "Worth {amount}. {expiry}",
  "sell.expires": "Valid until {date}.",
  "sell.noExpiry": "Does not expire.",
  "sell.done": "I have given the code to the customer",
  "check.code": "Voucher code",
  "check.button": "Check balance",
  "check.balance": "Voucher ending {last4} has {amount} left.",
  "check.expires": "Valid until {date}.",
  "check.void": "This voucher has been cancelled.",
  "check.expired": "This voucher has expired.",
  "redeem.code": "Voucher code",
  "redeem.amount": "Amount to take off the voucher",
  "redeem.button": "Use voucher",
  "redeem.done": "Took {amount} off the voucher. {balance} left.",
  "redeem.reference": "Payment reference for the sale: {reference}",
  "redeem.referenceNote": "A sale paid with this voucher uses this reference.",
  "redeem.invalid": "Type the amount in rand, for example 50 or 50,50.",
  "code.required": "Type or scan the voucher code.",
} as const;

export type VouchersKey = keyof typeof en;
export const messages: AreaMessages<VouchersKey> = { en };
