import { defineFeature } from "../../feature";
import { LaybyTab } from "./Layby";
import { messages } from "./messages";

export const feature = defineFeature({ id: "layby", order: 230, title: "title", messages, flag: "layby", requiresOnline: true, component: LaybyTab });
