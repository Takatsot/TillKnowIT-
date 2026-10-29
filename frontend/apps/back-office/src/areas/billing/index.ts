import { defineSection } from "@tillknowit/app-kit";
import { Billing } from "./Billing";
import { messages } from "./messages";

export const section = defineSection({ id: "billing", order: 80, title: "title", messages, needs: ["personId", "shopId"], component: Billing });
