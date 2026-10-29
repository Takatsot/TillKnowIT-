import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Pre-orders",
  loading: "Loading...",
  reload: "Try again",
  placeHeading: "Order for a break",
  placeHelp: "The money is taken from the wallet now. You get it all back if you cancel before the cut-off time.",
  noMenu: "The school has no menu or breaks yet.",
  break: "Break",
  breakOption: "{name} (order before {cutOff})",
  day: "Day",
  dayHint: "A school day, from today up to a week ahead.",
  menu: "Menu",
  quantity: "{name}: how many?",
  total: "Total: {amount}",
  nothingChosen: "Choose at least one item.",
  place: "Place order",
  placed: "Order placed. Total {amount}.",
  listHeading: "My orders",
  listEmpty: "No orders in the last week or coming up.",
  line: "{quantity} x {name}",
  cutOff: "Closes {time}",
  cancel: "Cancel order",
  cancelLabel: "Cancel the {break} order for {day}",
  cancelled: "The order was cancelled and the money is back in the wallet.",
  statusplaced: "Placed",
  statusready: "Ready",
  statuscollected: "Collected",
  statuscancelled: "Cancelled",
  orderTitle: "{break} on {day}",
} as const;

export type PreOrderKey = keyof typeof en;
export const messages: AreaMessages<PreOrderKey> = { en };
