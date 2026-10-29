import { defineSection } from "@tillknowit/app-kit";
import { Statement } from "./Statement";
import { messages } from "./messages";

export const section = defineSection({ id: "statement", order: 40, title: "title", messages, needs: ["personId"], component: Statement });
