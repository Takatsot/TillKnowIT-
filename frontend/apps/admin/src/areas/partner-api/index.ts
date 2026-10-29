import { defineSection } from "@tillknowit/app-kit";
import { PartnerApi } from "./PartnerApi";
import { messages } from "./messages";

export const section = defineSection({ id: "partner-api", order: 60, title: "title", messages, needs: ["personId"], component: PartnerApi });
