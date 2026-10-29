import { defineSection } from "@tillknowit/app-kit";
import { CreditBook } from "./CreditBook";
import { messages } from "./messages";

export const section = defineSection({ id: "credit-book", order: 30, title: "title", messages, needs: ["shopId"], component: CreditBook });
