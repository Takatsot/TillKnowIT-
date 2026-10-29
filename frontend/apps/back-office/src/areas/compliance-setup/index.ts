import { defineSection } from "@tillknowit/app-kit";
import { ComplianceSetup } from "./ComplianceSetup";
import { messages } from "./messages";

export const section = defineSection({ id: "compliance-setup", order: 50, title: "title", messages, needs: ["personId", "shopId"], component: ComplianceSetup });
