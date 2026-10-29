import type { TillFeature } from "./feature";

/** Every folder under ./features that exports a `feature` is a tab of the till. */
const found = import.meta.glob<{ feature: TillFeature<string> }>("./features/*/index.ts", { eager: true });

export const tillFeatures: TillFeature<string>[] = Object.values(found).map((m) => m.feature).sort((a, b) => a.order - b.order);
