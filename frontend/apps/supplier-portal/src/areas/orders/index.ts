import { defineSection } from "@tillknowit/app-kit";
import { messages } from "./messages";
import { OrdersArea } from "./OrdersArea";

export const section = defineSection({ id: "orders", order: 30, title: "title", messages, needs: ["personId"], component: OrdersArea });
