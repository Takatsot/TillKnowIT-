import { defineFeature } from "../../feature";
import { Appointments } from "./Appointments";
import { messages } from "./messages";

export const feature = defineFeature({ id: "appointments", order: 260, title: "title", messages, flag: "appointments", requiresOnline: true, component: Appointments });
