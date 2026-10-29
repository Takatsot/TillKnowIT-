import { defineSection } from "@tillknowit/app-kit";
import { SpendingRules } from "./SpendingRules";
import { messages } from "./messages";

export const section = defineSection({ id: "spending-rules", order: 30, title: "title", messages, needs: ["personId"], component: SpendingRules });
