import { defineSection } from "@tillknowit/app-kit";
import { Reports } from "./Reports";
import { messages } from "./messages";

export const section = defineSection({ id: "reports", order: 25, title: "title", messages, needs: ["personId", "shopId"], component: Reports });
