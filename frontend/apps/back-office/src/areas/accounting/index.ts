import { defineSection } from "@tillknowit/app-kit";
import { Accounting } from "./Accounting";
import { messages } from "./messages";

export const section = defineSection({ id: "accounting", order: 81, title: "title", messages, needs: ["personId", "shopId"], component: Accounting });
