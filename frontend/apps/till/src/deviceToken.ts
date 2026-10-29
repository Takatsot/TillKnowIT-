/**
 * The till's credential. The owner's setup code (typed at registration) is kept only until the first check-in has traded it for a token; after that the token is
 * sent as `X-Device-Token` on every call and replaced by the fresh one every check-in returns. Neither is ever shown or logged.
 */
export const DEVICE_TOKEN_KEY = "tillknowit.deviceToken";
export const ENROLMENT_CODE_KEY = "tillknowit.enrolmentCode";

function read(key: string): string | null {
  try {
    const value = localStorage.getItem(key);
    return value ? value : null;
  } catch {
    return null;
  }
}

function write(key: string, value: string): boolean {
  try {
    localStorage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

function remove(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing to clear.
  }
}

export const loadDeviceToken = (): string | null => read(DEVICE_TOKEN_KEY);
export const saveDeviceToken = (token: string): boolean => write(DEVICE_TOKEN_KEY, token);
export const clearDeviceToken = (): void => remove(DEVICE_TOKEN_KEY);

export const loadEnrolmentCode = (): string | null => read(ENROLMENT_CODE_KEY);
export const saveEnrolmentCode = (code: string): boolean => write(ENROLMENT_CODE_KEY, code);
export const clearEnrolmentCode = (): void => remove(ENROLMENT_CODE_KEY);

/** Forgets both. Used when the till is wiped. */
export function clearDeviceCredential(): void {
  clearDeviceToken();
  clearEnrolmentCode();
}

/** For `createApi({ headers })` and the sales upload: read on every request, so a refreshed token is used straight away. */
export function deviceHeaders(): Record<string, string> {
  const token = loadDeviceToken();
  return token ? { "X-Device-Token": token } : {};
}
