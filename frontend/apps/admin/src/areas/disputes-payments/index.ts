import { defineSection } from "@tillknowit/app-kit";
import { DisputesPayments } from "./DisputesPayments";
import { messages } from "./messages";

export const section = defineSection({ id: "disputes-payments", order: 40, title: "title", messages, needs: ["personId"], component: DisputesPayments });
