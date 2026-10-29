import { defineSection } from "@tillknowit/app-kit";
import { PayLater } from "./PayLater";
import { messages } from "./messages";

export const section = defineSection({ id: "pay-later", order: 82, title: "title", messages, needs: ["personId", "shopId"], component: PayLater });
