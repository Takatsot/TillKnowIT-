import { defineSection } from "@tillknowit/app-kit";
import { DealsArea } from "./DealsArea";
import { messages } from "./messages";

export const section = defineSection({ id: "deals", order: 40, title: "title", messages, needs: ["personId"], component: DealsArea });
