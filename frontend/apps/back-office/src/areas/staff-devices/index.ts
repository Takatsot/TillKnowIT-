import { defineSection } from "@tillknowit/app-kit";
import { StaffDevices } from "./StaffDevices";
import { messages } from "./messages";

export const section = defineSection({ id: "staff-devices", order: 50, title: "title", messages, needs: ["personId", "shopId"], component: StaffDevices });
