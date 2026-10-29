import type { AreaMessages } from "@tillknowit/i18n";

const en = {
  title: "Application",
  detailsHeading: "Your supplier",
  supplierId: "Supplier id",
  supplierIdHint: "Filled in for you after you apply. The server has no way to find it from your organisation, so keep it safe.",
  look: "Show my supplier",
  noSupplier: "No supplier chosen yet. Apply below, or type the supplier id you were given.",
  loading: "Loading...",
  verified: "Verified",
  yes: "Yes",
  no: "Not yet",
  rejection: "Why it was not accepted: {reason}",
  minOrder: "Minimum order",
  deliveryFee: "Delivery fee",
  paymentTerms: "Payment terms",
  areas: "Areas and delivery days",
  noAreas: "No delivery areas yet.",
  applyHeading: "Apply to become a supplier",
  applyIntro: "We check your details before you can sell. You will see the status here once you have applied.",
  bizName: "Business name",
  registrationNo: "Registration number (optional)",
  contactName: "Contact person",
  phone: "Phone (optional)",
  whatsapp: "WhatsApp (optional)",
  email: "Email (optional)",
  areaCode: "Municipality code",
  areaCodeHint: "For example JHB.",
  days: "Delivery days",
  addArea: "Add another area",
  removeArea: "Remove area {n}",
  submit: "Send application",
  applied: "Application sent. Status: {status}. Your supplier id {id} is saved in this browser.",
  needArea: "Add at least one area with a delivery day.",
} as const;

export type ApplicationKey = keyof typeof en;
export const messages: AreaMessages<ApplicationKey> = { en };
