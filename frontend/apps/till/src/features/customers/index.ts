import { defineFeature } from "../../feature";
import { Customers } from "./Customers";
import { messages } from "./messages";

export const feature = defineFeature({ id: "customers", order: 250, title: "title", messages, flag: "customers", requiresOnline: true, component: Customers });
