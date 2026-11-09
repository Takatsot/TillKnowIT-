import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Shops to set up",
  loading: "Loading...",
  tryAgain: "Try again",

  listHeading: "Shops waiting for you",
  listHelp: "New shops stay pending until every step is done and you verify them. The ones furthest along come first.",
  listEmpty: "No shops are waiting for setup.",
  colShop: "Shop",
  colArea: "Municipality",
  colProgress: "Steps done",
  progress: "{done} of {total}",
  open: "Open {name}",
  openShort: "Open",

  back: "Back to the list",
  stepsHeading: "Setup steps for {name}",
  colStep: "Step",
  colDone: "State",
  colNote: "Note",
  colActions: "Actions",
  done: "Done",
  notDone: "Still to do",
  noteLabel: "Note for the next step you mark (optional)",
  noteHint: "For example: signed by the owner, or 12 products loaded.",
  markDone: "Mark done",
  markDoneFor: "Mark {step} done",
  stepSaved: "{step} is marked done.",
  step_device_check: "Device check",
  step_shop_details: "Shop details",
  step_document_photos: "Document photos",
  step_catalogue_import: "Catalogue import",
  step_staff_pins: "Staff PINs",
  step_test_sale: "Test sale",
  step_training_signoff: "Training sign-off",

  verify: "Verify this shop",
  verifyNotReady: "Every step must be done before you can verify the shop.",
  verified: "{name} is verified.",
  alreadyVerified: "This shop is already verified.",
} as const;

export type SetupKey = keyof typeof en;
export const messages: AreaMessages<SetupKey> = { en };
