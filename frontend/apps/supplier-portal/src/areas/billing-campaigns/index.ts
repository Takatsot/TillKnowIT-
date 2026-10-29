import { defineSection } from "@tillknowit/app-kit";
import { BillingCampaigns } from "./BillingCampaigns";
import { messages } from "./messages";

export const section = defineSection({ id: "billing-campaigns", order: 50, title: "title", messages, needs: ["personId", "supplierOrganisationId"], component: BillingCampaigns });
