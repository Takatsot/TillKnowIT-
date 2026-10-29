import { defineFeature } from "../../feature";
import { CoreTransfers } from "./CoreTransfers";
import { messages } from "./messages";

/** Sending and receiving stock between branches: owner and manager, online only (the server makes the stock movements). */
export const feature = defineFeature({ id: "core-transfers", order: 140, title: "title", messages, roles: ["owner", "manager"], requiresOnline: true, component: CoreTransfers });
