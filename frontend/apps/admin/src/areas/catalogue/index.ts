import { defineSection } from "@tillknowit/app-kit";
import { Catalogue } from "./Catalogue";
import { messages } from "./messages";

export const section = defineSection({ id: "catalogue", order: 70, title: "title", messages, needs: ["personId"], component: Catalogue });
