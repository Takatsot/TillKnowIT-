import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Insights and partners",
  loading: "Loading...",
  planNote: "This is part of a paid plan. See Billing to choose one.",
  insightsHeading: "Market insights",
  optIn: "Include my shop in market insights",
  optInState: "Your shop is {state}.",
  stateIn: "included",
  stateOut: "not included",
  changedOn: "Last changed {date}.",
  optInSaved: "Your choice was saved.",
  partnersHeading: "Partner access to your sales",
  partnersExplain: "A partner can read your sales figures only for the kinds of data you approve here. You can withdraw access at any time.",
  grantHeading: "Approve a partner",
  clientId: "Partner client id",
  clientHint: "The partner gives you this id. Partners are registered by us.",
  scopes: "What the partner may read",
  scope_sales_daily: "Daily sales totals",
  scope_sales_monthly: "Monthly sales totals",
  grant: "Approve access",
  granted: "{partner} can now read what you approved.",
  needScope: "Choose at least one kind of data.",
  grantsHeading: "Approved partners",
  grantsEmpty: "No partner has access to your sales.",
  colPartner: "Partner",
  colScopes: "May read",
  colApproved: "Approved on",
  colState: "State",
  stateActive: "Active",
  stateWithdrawn: "Withdrawn",
  withdraw: "Withdraw access for {partner}",
  withdrawn: "Access for {partner} was withdrawn.",
} as const;

export type InsightsKey = keyof typeof en;
export const messages: AreaMessages<InsightsKey> = { en };
