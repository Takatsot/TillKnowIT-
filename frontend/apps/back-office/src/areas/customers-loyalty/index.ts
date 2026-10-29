import { defineSection } from "@tillknowit/app-kit";
import { CustomersLoyalty } from "./CustomersLoyalty";
import { messages } from "./messages";

export const section = defineSection({ id: "customers-loyalty", order: 75, title: "title", messages, needs: ["personId", "shopId"], component: CustomersLoyalty });
