import type { Api } from "@tillknowit/api-client";
import { formatRand, SessionProvider, type Session } from "@tillknowit/app-kit";
import { configure, render } from "@testing-library/react";
import type { ReactElement } from "react";
import { vi } from "vitest";
import type { Child } from "./types";

export const session: Session = { personId: "p1", shopId: "", supplierOrganisationId: "", deviceId: "", language: "en" };

export const thandi: Child = {
  holderId: "h1", walletId: "w1", name: "Thandi Dlamini", grade: "5", className: "5B", school: "Sunrise Primary", balanceCents: 5000, dailyLimitCents: 2000, walletStatus: "active",
};
export const sipho: Child = { ...thandi, holderId: "h2", walletId: "w2", name: "Sipho Dlamini", balanceCents: 1000, dailyLimitCents: null };

/** Mounts a component with a fake Api (only the calls a test gives are available). */
export function mount(ui: ReactElement, api: Partial<Api>) {
  return render(<SessionProvider api={api as Api} initial={session}>{ui}</SessionProvider>);
}

/** Money the way the screens write it (rand, with the space the formatter uses), so tests do not depend on the exact character. */
export const rand = (text: string): string => text.replace(/R(\d[\d,]*)/g, (_, n: string) => formatRand(Math.round(Number(n.replace(",", ".")) * 100)).replace(/ /g, " "));

// The first render in a file is slow on a busy machine; a generous limit stops that being reported as a failure.
vi.setConfig({ testTimeout: 30_000 });
configure({ asyncUtilTimeout: 10_000 });
