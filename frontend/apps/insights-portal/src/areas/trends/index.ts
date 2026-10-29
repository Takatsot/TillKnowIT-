import { defineSection } from "@tillknowit/app-kit";
import { messages } from "./messages";
import { Trends } from "./Trends";

export const section = defineSection({ id: "trends", order: 10, title: "title", messages, needs: ["personId", "supplierOrganisationId"], component: Trends });
