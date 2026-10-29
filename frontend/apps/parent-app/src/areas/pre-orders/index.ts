import { defineSection } from "@tillknowit/app-kit";
import { PreOrders } from "./PreOrders";
import { messages } from "./messages";

export const section = defineSection({ id: "pre-orders", order: 50, title: "title", messages, needs: ["personId"], component: PreOrders });
