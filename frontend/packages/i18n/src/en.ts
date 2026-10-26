/** English is the source of truth: every key lives here first. Other languages are partial until translated. */
export const en = {
  "app.name": "TillKnowIT",
  "app.language": "Language",

  "unlock.title": "Who is selling?",
  "unlock.enterPin": "Enter your PIN",
  "unlock.pinFor": "PIN for {name}",
  "unlock.wrongPin": "That PIN is not right. Tries left: {left}.",
  "unlock.locked": "Too many wrong tries. Try again in {minutes} min.",
  "unlock.changeStaff": "Not you?",
  "unlock.delete": "Delete",
  "unlock.ok": "OK",
  "unlock.noStaff": "No staff on this till yet. Ask the owner to add you.",

  "till.hello": "Hello, {name}",
  "till.unlocked": "The till is unlocked. Selling comes next.",
  "till.lock": "Lock till",

  "status.offline": "Working offline",
  "status.online": "Online",
} as const;

export type MessageKey = keyof typeof en;
