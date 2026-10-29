import { defineSection } from "@tillknowit/app-kit";
import { Packs } from "./Packs";
import { messages } from "./messages";

export const section = defineSection({ id: "packs", order: 10, title: "title", messages, needs: ["personId"], component: Packs });
