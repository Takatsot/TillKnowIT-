import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { Quotes } from "./Quotes";

export const feature = defineFeature({ id: "quotes", order: 220, title: "title", messages, flag: "quotes", requiresOnline: true, component: Quotes });
