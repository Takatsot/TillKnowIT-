import { defineFeature } from "../../feature";
import { Kitchen } from "./Kitchen";
import { messages } from "./messages";

export const feature = defineFeature({ id: "kitchen", order: 210, title: "title", messages, flag: "kitchen", requiresOnline: true, component: Kitchen });
