import { translateArea, type AreaMessages, type LanguageCode } from "@tillknowit/i18n";

export type ShellKey =
  | "app" | "language" | "missing" | "noSections"
  | "signInTitle" | "phone" | "phoneHint" | "sendCode" | "code" | "codeHint" | "signInButton" | "differentNumber" | "devOnly" | "devCode"
  | "otp.phone_invalid" | "otp.too_many" | "otp.invalid" | "codeSent"
  | "sessionEnded" | "sessionExpired"
  | "signedInAs" | "signOut" | "workingFor" | "workingForNone" | "membershipsFailed" | "orgShop" | "orgSupplier" | "orgOther"
  | "advanced" | "advancedHint" | "shopId" | "supplierId" | "deviceId" | "save";

export const shellMessages: AreaMessages<ShellKey> = {
  en: {
    app: "TillKnowIT", language: "Language", missing: "Fill in {what} to use this part.", noSections: "Nothing is available yet.",
    signInTitle: "Sign in", phone: "Your cell phone number", phoneHint: "For example 082 123 4567. We will send you a code.", sendCode: "Send me a code",
    code: "The code we sent you", codeHint: "Check your messages for the code.", signInButton: "Sign in", differentNumber: "Use a different number",
    devOnly: "Development only", devCode: "The server sent the code back so you can test without a phone: {code}. This never happens in the real service.",
    "otp.phone_invalid": "That does not look like a cell phone number. Check it and try again.",
    "otp.too_many": "Too many tries. Please wait a few minutes and try again.",
    "otp.invalid": "That code is not right, or it has expired. Check it or ask for a new code.",
    codeSent: "If that number has an account, a code is on its way.",
    sessionEnded: "Please sign in again.", sessionExpired: "Your sign-in has run out. Please sign in again.",
    signedInAs: "Signed in as", signOut: "Sign out", workingFor: "Who are you working for?", workingForNone: "You are not part of any shop or supplier yet.",
    membershipsFailed: "We could not load your shops and suppliers.", orgShop: "shop", orgSupplier: "supplier", orgOther: "organisation",
    advanced: "Advanced: type ids by hand", advancedHint: "Only for testing. Normally the shop and supplier come from who you work for above.",
    shopId: "Shop id", supplierId: "Supplier organisation id", deviceId: "Device id", save: "Save",
  },
};

export const shellText = (language: LanguageCode, key: ShellKey, params?: Record<string, string | number>) => translateArea(shellMessages, language, key, params);
