import { defineFeature } from "../../feature";
import { CoreCashUp } from "./CoreCashUp";
import { messages } from "./messages";

/** End-of-day cash-up. Every role may do it (cashiers have cashup.do). Works offline: it reads the till's own books and queues the cash-up. */
export const feature = defineFeature({ id: "core-cashup", order: 110, title: "title", messages, component: CoreCashUp });
