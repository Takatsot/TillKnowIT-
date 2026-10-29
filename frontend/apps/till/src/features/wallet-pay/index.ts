import { defineFeature } from "../../feature";
import { messages } from "./messages";
import { WalletPay } from "./WalletPay";

/**
 * No `flag`: the business pack has no school-wallet flag (see featureFlags in @tillknowit/packs), so the tab is offered to every till and the server answers
 * "wallet.no_school" ("This shop does not take wallet payments.") for a shop that is not a school tuckshop.
 */
export const feature = defineFeature({ id: "wallet-pay", order: 280, title: "title", messages, requiresOnline: true, component: WalletPay });
