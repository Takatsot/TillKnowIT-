import { defineSection } from "@tillknowit/app-kit";
import { messages } from "./messages";
import { Plan } from "./Plan";

export const section = defineSection({ id: "plan", order: 20, title: "title", messages, needs: ["personId", "supplierOrganisationId"], component: Plan });
