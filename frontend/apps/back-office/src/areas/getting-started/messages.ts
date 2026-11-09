import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Getting started",
  loading: "Loading...",

  createHeading: "Create your shop",
  createIntro: "Tell us about your shop. You will be its owner. A TillKnowIT field agent then helps you finish setting up and verifies it.",
  shopName: "Shop name",
  area: "Your municipality",
  areaChoose: "Choose your municipality",
  areaHint: "Where the shop is. You can change this later.",
  language: "Language for the shop",
  create: "Create my shop",
  nameRequired: "Type the name of your shop.",
  created: "{name} was created.",

  progressHeading: "Your shop's setup",
  progress: "{done} of {total} setup steps done",
  statusPending: "Waiting for verification",
  statusVerified: "Verified",
  statusSuspended: "Suspended",
  pendingHelp: "A TillKnowIT field agent will visit or call to finish the steps below and verify your shop. You can already add your products and staff while you wait.",
  verifiedHelp: "Your shop is verified. Everything is switched on.",
  stepsHeading: "What the field agent checks",
  colStep: "Step",
  colState: "State",
  done: "Done",
  notDone: "Still to do",
  step_device_check: "Device check: the till or phone works",
  step_shop_details: "Shop details are correct",
  step_document_photos: "Photos of your documents",
  step_catalogue_import: "Your products are loaded",
  step_staff_pins: "Staff have their PINs",
  step_test_sale: "A test sale went through",
  step_training_signoff: "You have been shown how to use it",

  nextHeading: "What you can do now",
  next1: "Products and stock: add the things you sell, or ask the field agent to load them.",
  next2: "Staff and tills: add your staff and register your tills.",
  next3: "Business type: choose what kind of shop you run, so the till shows the right tabs.",
} as const;

export type GettingStartedKey = keyof typeof en;
export const messages: AreaMessages<GettingStartedKey> = { en };
