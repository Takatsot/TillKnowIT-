import { defineSection } from "@tillknowit/app-kit";
import { ModifiersAndFloor } from "./ModifiersAndFloor";
import { messages } from "./messages";

export const section = defineSection({ id: "modifiers-and-floor", order: 65, title: "title", messages, needs: ["personId", "shopId"], component: ModifiersAndFloor });
