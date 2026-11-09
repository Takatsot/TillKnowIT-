import { defineSection } from "@tillknowit/app-kit";
import { SetupShops } from "./SetupShops";
import { messages } from "./messages";

// A field agent works across shops, so this needs only the person: the shop is chosen on the screen.
export const section = defineSection({ id: "setup", order: 10, title: "title", messages, needs: ["personId"], component: SetupShops });
