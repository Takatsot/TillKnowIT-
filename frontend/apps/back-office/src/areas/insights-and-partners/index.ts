import { defineSection } from "@tillknowit/app-kit";
import { InsightsAndPartners } from "./InsightsAndPartners";
import { messages } from "./messages";

export const section = defineSection({ id: "insights-and-partners", order: 83, title: "title", messages, needs: ["personId", "shopId"], component: InsightsAndPartners });
