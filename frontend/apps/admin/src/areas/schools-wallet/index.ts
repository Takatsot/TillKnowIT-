import { defineSection } from "@tillknowit/app-kit";
import { SchoolsWallet } from "./SchoolsWallet";
import { messages } from "./messages";

export const section = defineSection({ id: "schools-wallet", order: 50, title: "title", messages, needs: ["personId"], component: SchoolsWallet });
