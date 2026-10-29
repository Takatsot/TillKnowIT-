import { defineSection } from "@tillknowit/app-kit";
import { Branches } from "./Branches";
import { messages } from "./messages";

export const section = defineSection({ id: "branches", order: 40, title: "title", messages, needs: ["personId", "shopId"], component: Branches });
