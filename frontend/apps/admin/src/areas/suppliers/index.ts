import { defineSection } from "@tillknowit/app-kit";
import { Suppliers } from "./Suppliers";
import { messages } from "./messages";

export const section = defineSection({ id: "suppliers", order: 30, title: "title", messages, needs: ["personId"], component: Suppliers });
