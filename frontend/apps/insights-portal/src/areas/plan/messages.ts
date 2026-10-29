import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Plan",
  loading: "Loading...",
  subHeading: "Your subscription",
  noSub: "You have no subscription yet, so you are on the free plan. Market trend reports are not part of the free plan.",
  subLine: "Plan {plan}, status {status}. Current period {from} to {to}.",
  pending: "Changes to {plan} when this period ends.",
  cancelling: "Cancels at the end of this period.",
  plansHeading: "Plans",
  plansEmpty: "No plans are available right now.",
  colPlan: "Plan",
  colPrice: "Price per month",
  colIncludes: "Includes",
  free: "Free",
  includesNothing: "The basics",
  current: "Your plan",
  f_insight_reports: "Market trend reports",
  f_supplier_analytics: "Supplier analytics",
  f_sponsored: "Sponsored campaigns",
  f_partner_api: "Partner API",
  choose: "Choose {name}",
  chooseShort: "Choose",
  subscribed: "You are now on {plan}.",
} as const;

export type PlanKey = keyof typeof en;
export const messages: AreaMessages<PlanKey> = { en };
