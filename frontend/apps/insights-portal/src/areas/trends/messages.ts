import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Market trends",
  heading: "Weekly trends for an area",
  intro: "Ask for the weekly trend of an area, and of one category if you like. Figures are grouped, so no shop is ever shown.",
  area: "Municipality code",
  areaHint: "For example JHB.",
  category: "Category (optional)",
  from: "From",
  to: "To",
  request: "Show trends",
  busy: "Working...",
  results: "Trends for {area}",
  colWeek: "Week starting",
  colCategory: "Category",
  colUnits: "Units sold",
  colRevenue: "Revenue band",
  colShops: "Shops behind it",
  empty: "No weeks to show for this area and period.",
  needArea: "Type the municipality code of the area.",
  needDates: "Choose the first and last day.",
  minHeading: "Why some areas show nothing",
  minRule: "We only publish a figure when at least 10 shops are behind it, and only shops that chose to share. Revenue is shown as a band, not an exact amount, and no shop is named. If an area or category has fewer than 10 shops, its weeks are left out so a figure can never point at one shop. Try a bigger area, a wider period or no category.",
  shopsNote: "Shops behind it is the lowest number of shops in that week, and is always 10 or more.",
  planTitle: "Market insights are not in your plan",
  planBody: "Trend reports are part of the Plus and Premium supplier plans. Open Plan in the menu to see the plans and upgrade, then come back here.",
} as const;

export type TrendsKey = keyof typeof en;
export const messages: AreaMessages<TrendsKey> = { en };
