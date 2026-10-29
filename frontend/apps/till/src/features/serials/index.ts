import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { Serials } from "./Serials";

export const feature = defineFeature({ id: "serials", order: 270, title: "title", messages, flag: "serial_numbers", requiresOnline: true, component: Serials });
