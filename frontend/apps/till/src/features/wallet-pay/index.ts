import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { WalletPay } from "./WalletPay";

/** Only shops whose pack has the school wallet switched on get this tab. */
export const feature = defineFeature({ id: "wallet-pay", order: 280, title: "title", messages, flag: "school_wallet", requiresOnline: true, component: WalletPay });
