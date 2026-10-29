import { defineSection } from "@tillknowit/app-kit";
import { ServicesAppointments } from "./ServicesAppointments";
import { messages } from "./messages";

export const section = defineSection({ id: "services-appointments", order: 70, title: "title", messages, needs: ["personId", "shopId"], component: ServicesAppointments });
