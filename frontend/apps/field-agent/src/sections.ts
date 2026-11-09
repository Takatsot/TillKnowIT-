import type { Section } from "@tillknowit/app-kit";

/**
 * Every folder under ./areas that exports a `section` is a menu entry. Adding an area means adding a folder, never editing this file.
 */
const found = import.meta.glob<{ section: Section<string> }>("./areas/*/index.ts", { eager: true });

export const sections: Section<string>[] = Object.values(found).map((m) => m.section);
