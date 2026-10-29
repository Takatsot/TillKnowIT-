import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { Tables } from "./Tables";

export const feature = defineFeature({ id: "tables", order: 200, title: "title", messages, flag: "tables", requiresOnline: true, component: Tables });
