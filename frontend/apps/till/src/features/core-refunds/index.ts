import { defineFeature } from "../../feature";
import { CoreRefunds } from "./CoreRefunds";
import { messages } from "./messages";

/** Refunds and cancellations of a sale made on this till. Works offline: it reads the till's own books and queues what it does. */
export const feature = defineFeature({ id: "core-refunds", order: 100, title: "title", messages, component: CoreRefunds });
