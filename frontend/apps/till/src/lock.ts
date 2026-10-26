/** Whole minutes left on a lock, never less than 1 while it is still locked. */
export function minutesLeft(lockedUntil: number, now: number): number {
  return Math.max(1, Math.ceil((lockedUntil - now) / 60_000));
}
