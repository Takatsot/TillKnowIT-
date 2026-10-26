/** Same shape idea as the C# Result: a code the app can switch on and a message for people. */
export type Ok<T> = { ok: true; value: T };
export type Fail = { ok: false; code: string; message: string };
export type Result<T> = Ok<T> | Fail;

export const ok = <T>(value: T): Ok<T> => ({ ok: true, value });
export const fail = (code: string, message: string): Fail => ({ ok: false, code, message });
