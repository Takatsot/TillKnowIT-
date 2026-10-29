import { defineSection } from "@tillknowit/app-kit";
import { Requests } from "./Requests";
import { messages } from "./messages";

export const section = defineSection({ id: "requests", order: 60, title: "title", messages, needs: ["personId"], component: Requests });
