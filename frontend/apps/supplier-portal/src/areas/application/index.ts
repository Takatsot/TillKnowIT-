import { defineSection } from "@tillknowit/app-kit";
import { Application } from "./Application";
import { messages } from "./messages";

export const section = defineSection({ id: "application", order: 10, title: "title", messages, needs: ["personId"], component: Application });
