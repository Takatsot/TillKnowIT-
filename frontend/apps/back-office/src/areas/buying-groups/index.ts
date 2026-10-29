import { defineSection } from "@tillknowit/app-kit";
import { BuyingGroups } from "./BuyingGroups";
import { messages } from "./messages";

export const section = defineSection({ id: "buying-groups", order: 84, title: "title", messages, needs: ["personId", "shopId"], component: BuyingGroups });
