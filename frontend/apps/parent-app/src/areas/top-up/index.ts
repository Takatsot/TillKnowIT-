import { defineSection } from "@tillknowit/app-kit";
import { TopUp } from "./TopUp";
import { messages } from "./messages";

export const section = defineSection({ id: "top-up", order: 20, title: "title", messages, needs: ["personId"], component: TopUp });
