import { defineSection } from "@tillknowit/app-kit";
import { Children } from "./Children";
import { messages } from "./messages";

export const section = defineSection({ id: "children", order: 10, title: "title", messages, needs: ["personId"], component: Children });
