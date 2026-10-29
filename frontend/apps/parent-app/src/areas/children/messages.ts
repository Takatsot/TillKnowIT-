import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "My children",
  heading: "My children",
  loading: "Loading...",
  empty: "No child is linked to you yet. Add one below.",
  balance: "Balance",
  dailyLimit: "Daily limit",
  noLimit: "No daily limit",
  frozen: "This wallet is not active.",
  classLine: "Grade {grade}, class {className}",
  add: "Add a child",
  addHelp: "The school gives you an 8 letter code for your child. Type it here.",
  code: "Link code",
  consent: "I agree that TillKnowIT may keep my child's name, photo, purchases and balance so the school wallet can work.",
  link: "Add child",
  linked: "{name} was added.",
  schoolDoesHeading: "Changes the school makes",
  schoolDoes: "Your child's name, grade, class and photo, new cards and wristbands, and marking a child as having left are all done by the school. Photos are kept at the school tills only. If something needs to change, ask the school.",
  reload: "Try again",
} as const;

export type ChildrenKey = keyof typeof en;
export const messages: AreaMessages<ChildrenKey> = { en };
/** Names the consent text the parent was shown. The server only needs a non-empty version. */
export const CONSENT_VERSION = "parent-consent-v1";
