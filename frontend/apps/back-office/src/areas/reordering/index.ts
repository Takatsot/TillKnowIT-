import { defineSection } from "@tillknowit/app-kit";
import { Reordering } from "./Reordering";
import { messages } from "./messages";

export const section = defineSection({ id: "reordering", order: 20, title: "title", messages, needs: ["personId", "shopId"], component: Reordering });
