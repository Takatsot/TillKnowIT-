import { defineSection } from "@tillknowit/app-kit";
import { GettingStarted } from "./GettingStarted";
import { messages } from "./messages";

// Needs only the person: a new owner has no shop yet, and this is where they make one.
export const section = defineSection({ id: "getting-started", order: 1, title: "title", messages, needs: ["personId"], component: GettingStarted });
