import { isUuid } from "@tillknowit/domain";
import { clearDeviceCredential } from "./deviceToken";
import { loadDeviceConfig, type DeviceConfig } from "./syncRunner";

/** Where the till keeps which shop and till it is. Same key `loadDeviceConfig` reads. */
export const DEVICE_KEY = "tillknowit.device";

export type RegistrationCheck = { ok: true; device: DeviceConfig } | { ok: false; error: "shop" | "device" };

/** The owner copies both ids from the back office. They are ids (GUIDs), so anything else is a typing mistake we can name. */
export function checkRegistration(shopId: string, deviceId: string): RegistrationCheck {
  const shop = shopId.trim().toLowerCase();
  const device = deviceId.trim().toLowerCase();
  if (!isUuid(shop)) return { ok: false, error: "shop" };
  if (!isUuid(device)) return { ok: false, error: "device" };
  return { ok: true, device: { shopId: shop, deviceId: device } };
}

export function saveDeviceConfig(device: DeviceConfig): boolean {
  try {
    localStorage.setItem(DEVICE_KEY, JSON.stringify(device));
    return loadDeviceConfig() !== null;
  } catch {
    return false;
  }
}

export function clearDeviceConfig(): void {
  try {
    localStorage.removeItem(DEVICE_KEY);
  } catch {
    // Nothing to clear.
  }
  clearDeviceCredential();
}
