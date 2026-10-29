import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Requests",
  refundHeading: "Get my money back",
  refundHelp: "When a child has left the school, you can ask for the money left in their wallet. The school checks it, then the money is paid back to you.",
  refundGo: "Ask for a refund",
  refundSent: "Your request was sent. Amount: {amount}. Status: {status}.",
  refundstatusrequested: "waiting for the school",
  refundstatusapproved: "approved",
  refundstatuspaid: "paid",
  refundstatusrejected: "not approved",
  leftAskSchool: "Only the school can mark your child as having left. If your child has left and the school has not done this yet, ask the school.",
  erasureHeading: "Remove my child's data",
  erasureHelp: "You can ask us to delete your child's name, photo and links to you. The record of the money stays, as the law says, but without any name. If there is still money in the wallet it is paid back to you first, then your child's data is removed. This takes at most 30 days.",
  erasureGo: "Remove my child's data",
  erasureSure: "Remove all of {name}'s data? This cannot be undone.",
  erasureYes: "Yes, remove it",
  erasureNo: "No, keep it",
  erasureDone: "{name}'s data has been removed.",
  erasureWaiting: "Your request was received. It will be done by {date}. Money left in the wallet must be refunded first.",
} as const;

export type RequestsKey = keyof typeof en;
export const messages: AreaMessages<RequestsKey> = { en };
