import { defineSection } from "@tillknowit/app-kit";
import { PlansBilling } from "./PlansBilling";
import { messages } from "./messages";

export const section = defineSection({ id: "plans-billing", order: 20, title: "title", messages, needs: ["personId"], component: PlansBilling });
