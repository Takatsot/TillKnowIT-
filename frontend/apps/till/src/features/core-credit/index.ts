import { defineFeature } from "../../feature";
import { CoreCredit } from "./CoreCredit";
import { messages } from "./messages";

/** Customers paying off credit. Every role may (cashiers have credit.payment). Works offline: the payment is queued like a sale. */
export const feature = defineFeature({ id: "core-credit", order: 120, title: "title", messages, component: CoreCredit });
