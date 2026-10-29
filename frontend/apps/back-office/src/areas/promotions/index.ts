import { defineSection } from "@tillknowit/app-kit";
import { Promotions } from "./Promotions";
import { messages } from "./messages";

export const section = defineSection({ id: "promotions", order: 60, title: "title", messages, needs: ["personId", "shopId"], component: Promotions });
