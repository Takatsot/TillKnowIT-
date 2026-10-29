import { defineFeature } from "../../feature";
import { CoreStock } from "./CoreStock";
import { messages } from "./messages";

/** Receiving, counting and writing off stock: owner and manager. These are stock movements sent through the outbox, so the tab works offline. */
export const feature = defineFeature({ id: "core-stock", order: 130, title: "title", messages, roles: ["owner", "manager"], component: CoreStock });
