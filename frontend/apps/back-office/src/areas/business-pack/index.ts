import { defineSection } from "@tillknowit/app-kit";
import { BusinessPack } from "./BusinessPack";
import { messages } from "./messages";

export const section = defineSection({ id: "business-pack", order: 55, title: "title", messages, needs: ["personId", "shopId"], component: BusinessPack });
