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
  edit: "Change details or photo",
  close: "Close",
  add: "Add a child",
  addHelp: "The school gives you an 8 letter code for your child. Type it here.",
  code: "Link code",
  consent: "I agree that TillKnowIT may keep my child's name, photo, purchases and balance so the school wallet can work.",
  link: "Add child",
  linked: "{name} was added.",
  details: "Details",
  detailsHelp: "Leave a box empty to keep it as it is.",
  firstName: "First name",
  lastName: "Surname",
  grade: "Grade",
  className: "Class",
  save: "Save",
  saved: "Details saved.",
  photo: "Photo",
  photoHelp: "A clear photo of the face, JPEG or PNG. The cashier checks it at the till.",
  photoSave: "Upload photo",
  photoSaved: "Photo saved.",
  photoNone: "Choose a photo first.",
  reload: "Try again",
} as const;

export type ChildrenKey = keyof typeof en;
export const messages: AreaMessages<ChildrenKey> = { en };
/** Names the consent text the parent was shown. The server only needs a non-empty version. */
export const CONSENT_VERSION = "parent-consent-v1";
