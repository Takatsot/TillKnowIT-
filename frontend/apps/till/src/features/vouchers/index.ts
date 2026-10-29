import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { Vouchers } from "./Vouchers";

export const feature = defineFeature({ id: "vouchers", order: 240, title: "title", messages, flag: "vouchers", requiresOnline: true, component: Vouchers });
